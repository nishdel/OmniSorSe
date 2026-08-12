using OpenSorSe.Core.Configuration;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.ContentIntelligence;

namespace OpenSorSe.Application.Indexing;

/// <summary>Defines durable deep-index schema and processor-version constants.</summary>
public static class DeepIndexingVersion
{
    /// <summary>Gets the currently supported provider-independent schema version.</summary>
    public const int SchemaVersion = 5;

    /// <summary>Gets the configuration version used to invalidate incompatible derived work.</summary>
    public const string ProcessorVersion = "2.3.0";
}

/// <summary>Identifies a durable stage in the background-indexing pipeline.</summary>
public enum IndexingStage
{
    /// <summary>The file was found and accepted by discovery policy.</summary>
    FileDiscovered,

    /// <summary>Path, name, timestamps, size, and basic metadata were indexed.</summary>
    MetadataIndexed,

    /// <summary>A bounded content fingerprint was calculated.</summary>
    ContentFingerprinted,

    /// <summary>Applicable native document text was extracted.</summary>
    TextExtracted,

    /// <summary>Applicable image-only text was processed by OCR.</summary>
    OcrProcessed,

    /// <summary>Bounded summary or keyword enrichment was generated.</summary>
    SummaryKeywordsGenerated,

    /// <summary>A document-level search representation was generated.</summary>
    SemanticRepresentationGenerated,

    /// <summary>The progressive search document was updated.</summary>
    SearchIndexUpdated,

    /// <summary>Safe duplicate-content and relationship information was updated.</summary>
    RelationshipAnalysisCompleted,

    /// <summary>All applicable stages for the selected policy completed.</summary>
    FileFullyIndexed,
}

/// <summary>Identifies the durable state of one stage or queued file.</summary>
public enum IndexingStageStatus
{
    /// <summary>The stage is known but not yet ready to run.</summary>
    Pending,

    /// <summary>The stage is ready to be claimed by a worker.</summary>
    Queued,

    /// <summary>A worker currently owns the stage.</summary>
    Running,

    /// <summary>The stage completed successfully.</summary>
    Complete,

    /// <summary>The stage was not applicable under the active policy.</summary>
    Skipped,

    /// <summary>The stage ended in a non-retryable failure.</summary>
    Failed,

    /// <summary>The stage is waiting for an optional tool or service.</summary>
    WaitingForDependency,

    /// <summary>The stage was safely cancelled by the user.</summary>
    Cancelled,

    /// <summary>The stage will become eligible again after its retry time.</summary>
    RetryScheduled,
}

/// <summary>Identifies the durable lifecycle state of an indexing run.</summary>
public enum IndexingRunStatus
{
    /// <summary>Discovery or queue preparation has not started.</summary>
    Pending,

    /// <summary>The run is accepting or processing work.</summary>
    Running,

    /// <summary>The run is durably paused.</summary>
    Paused,

    /// <summary>The run currently has only dependency-waiting work.</summary>
    Waiting,

    /// <summary>A cooperative cancellation request is being applied.</summary>
    Cancelling,

    /// <summary>The run was cancelled safely.</summary>
    Cancelled,

    /// <summary>Every queued file completed or was intentionally skipped.</summary>
    Complete,

    /// <summary>The run finished but retained one or more file failures.</summary>
    CompleteWithFailures,

    /// <summary>The run could not continue because of a run-level failure.</summary>
    Failed,
}

/// <summary>Classifies failures without retaining private file content.</summary>
public enum IndexingFailureCategory
{
    /// <summary>No failure is associated with the state.</summary>
    None,

    /// <summary>The file or source disappeared while work was pending.</summary>
    NotFound,

    /// <summary>The current user was not permitted to access the path.</summary>
    PermissionDenied,

    /// <summary>The file was temporarily locked by another process.</summary>
    FileLocked,

    /// <summary>An optional OCR or local-AI dependency was unavailable.</summary>
    DependencyUnavailable,

    /// <summary>The configured storage quota prevented derived data from being retained.</summary>
    StorageQuota,

    /// <summary>The file changed while it was being processed.</summary>
    FileChanged,

    /// <summary>The persisted index was malformed or failed an integrity check.</summary>
    StorageCorruption,

    /// <summary>The current provider cannot read the stored schema version.</summary>
    UnsupportedSchema,

    /// <summary>The stage was cancelled cooperatively.</summary>
    Cancelled,

    /// <summary>An expected transient I/O failure occurred.</summary>
    TransientIo,

    /// <summary>A deterministic input or processor failure will not succeed when retried unchanged.</summary>
    Permanent,

    /// <summary>An unexpected failure occurred and was safely isolated.</summary>
    Unexpected,
}

/// <summary>Describes one durable indexing source without exposing storage-provider details.</summary>
/// <param name="Id">Stable application identifier.</param>
/// <param name="RootPath">Absolute source root used only for local processing.</param>
/// <param name="DisplayName">Display-safe source name.</param>
/// <param name="Level">Requested indexing level.</param>
/// <param name="IncludeSubfolders">Whether discovery descends below the root.</param>
/// <param name="Enabled">Whether new work may be scheduled.</param>
/// <param name="Priority">Higher values are processed first.</param>
/// <param name="Exclusions">Source-relative glob-like exclusions.</param>
/// <param name="ManagedByWatchedFolders">Whether watched-folder configuration owns this source lifecycle.</param>
public sealed record IndexingSource(
    string Id,
    string RootPath,
    string DisplayName,
    IndexingLevel Level,
    bool IncludeSubfolders,
    bool Enabled,
    int Priority,
    IReadOnlyList<string> Exclusions,
    bool ManagedByWatchedFolders = false);

/// <summary>Describes one durable run that can continue after process restart.</summary>
public sealed record ResumableIndexingRun(
    string RunId,
    IndexingSource Source,
    IndexingRunStatus Status,
    bool DiscoveryComplete);

/// <summary>Describes one file observed during safe source discovery.</summary>
/// <param name="FullPath">Absolute path used for local processing.</param>
/// <param name="RelativePath">Source-relative path persisted for portability and display.</param>
/// <param name="StableIdentity">Best available file identity, or null when unavailable.</param>
/// <param name="FileSystemId">Stable file-system identifier associated with the identity.</param>
/// <param name="Length">Observed file size.</param>
/// <param name="CreationTimeUtc">Observed creation time.</param>
/// <param name="LastWriteTimeUtc">Observed modification time.</param>
/// <param name="Attributes">Observed file attributes.</param>
/// <param name="MetadataFingerprint">Deterministic fingerprint of path-independent basic metadata.</param>
public sealed record IndexingFileObservation(
    string FullPath,
    string RelativePath,
    string? StableIdentity,
    string? FileSystemId,
    long Length,
    DateTimeOffset CreationTimeUtc,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes,
    string MetadataFingerprint);

/// <summary>Describes one durable stage claim returned to an application worker.</summary>
public sealed record IndexingWorkItem
{
    /// <summary>Gets the durable job identifier.</summary>
    public required string JobId { get; init; }

    /// <summary>Gets the durable run identifier used by diagnostics and controls.</summary>
    public required string RunId { get; init; }

    /// <summary>Gets the durable file record identifier.</summary>
    public required string FileId { get; init; }

    /// <summary>Gets the source identifier.</summary>
    public required string SourceId { get; init; }

    /// <summary>Gets the local path being processed.</summary>
    public required string FullPath { get; init; }

    /// <summary>Gets the source-relative path.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Gets the selected indexing level.</summary>
    public required IndexingLevel Level { get; init; }

    /// <summary>Gets the durable stage claimed by the worker.</summary>
    public required IndexingStage Stage { get; init; }

    /// <summary>Gets the one-based attempt number.</summary>
    public required int Attempt { get; init; }

    /// <summary>Gets the configured processor fingerprint.</summary>
    public required string ProcessorFingerprint { get; init; }

    /// <summary>Gets the latest content hash, if already available.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Gets bounded extracted text needed by a later stage.</summary>
    public string? ExtractedText { get; init; }

    /// <summary>Gets bounded OCR text needed by a later stage.</summary>
    public string? OcrText { get; init; }

    /// <summary>Gets durable structured media evidence needed by later indexing stages.</summary>
    public IndexedMediaEvidence? MediaEvidence { get; init; }

    /// <summary>Gets bounded structured topics, textual entities, and source-grounded summary evidence.</summary>
    public IndexedContentIntelligence? ContentIntelligence { get; init; }

    /// <summary>Gets whether per-file policy disables OCR processing.</summary>
    public bool SuppressOcr { get; init; }

    /// <summary>Gets whether per-file policy disables generated summaries.</summary>
    public bool SuppressSummary { get; init; }

    /// <summary>Gets whether per-file policy disables related-concept data and chunks.</summary>
    public bool SuppressSemantic { get; init; }

    /// <summary>Gets whether explicit repair disables duplicate-content reuse for this run.</summary>
    public bool ForceReprocess { get; init; }

    /// <summary>Gets the file metadata observed when the job was queued.</summary>
    public required IndexingFileObservation Observation { get; init; }
}

/// <summary>Contains bounded derived values produced by one pipeline stage.</summary>
public sealed record IndexingStageOutput
{
    /// <summary>Gets the stage terminal or waiting state.</summary>
    public required IndexingStageStatus Status { get; init; }

    /// <summary>Gets a content hash calculated by the fingerprint stage.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Gets bounded native extracted text.</summary>
    public string? ExtractedText { get; init; }

    /// <summary>Gets bounded OCR text.</summary>
    public string? OcrText { get; init; }

    /// <summary>Gets bounded provider-neutral media evidence produced or reused by this stage.</summary>
    public IndexedMediaEvidence? MediaEvidence { get; init; }

    /// <summary>Gets bounded provider-neutral content intelligence produced or reused by this stage.</summary>
    public IndexedContentIntelligence? ContentIntelligence { get; init; }

    /// <summary>Gets a bounded non-sensitive summary when one was produced.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets bounded lightweight keywords.</summary>
    public IReadOnlyList<string>? Keywords { get; init; }

    /// <summary>Gets one bounded document-level search representation.</summary>
    public IReadOnlyList<float>? SemanticRepresentation { get; init; }

    /// <summary>Gets bounded selected chunks for Deep indexing.</summary>
    public IReadOnlyList<string>? SelectedChunks { get; init; }

    /// <summary>Gets a stable dependency name when work must wait.</summary>
    public string? WaitingDependency { get; init; }

    /// <summary>Gets a privacy-safe failure category.</summary>
    public IndexingFailureCategory FailureCategory { get; init; }

    /// <summary>Gets a privacy-safe, bounded error code or summary.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets whether an isolated failure may be retried.</summary>
    public bool IsRetryable { get; init; }

    /// <summary>Gets whether this terminal result intentionally stops remaining file stages.</summary>
    public bool StopsFile { get; init; }
}

/// <summary>Describes progressive Search coverage across known active files.</summary>
public sealed record SearchCoverage(
    long KnownFileCount,
    long FilenameAndMetadataCount,
    long ExtractedTextCount,
    long OcrCount,
    long SemanticCount,
    long FullyIndexedCount)
{
    /// <summary>Gets whether some known files are not yet fully indexed.</summary>
    public bool IsIncomplete => FullyIndexedCount < KnownFileCount;

    /// <summary>Gets the number of configured source exclusions and explicit file privacy exclusions.</summary>
    public long ExcludedSourceCount { get; init; }

    /// <summary>Gets the number of jobs waiting for OCR.</summary>
    public long WaitingForOcrCount { get; init; }

    /// <summary>Gets the number of jobs waiting for optional local AI.</summary>
    public long WaitingForAiCount { get; init; }

    /// <summary>Gets the number of retained failed stages.</summary>
    public long FailedStageCount { get; init; }

    /// <summary>Gets whether the progressive provider can currently serve Search records.</summary>
    public bool IsAvailable { get; init; } = true;
}

/// <summary>Describes retained storage by durable data category.</summary>
public sealed record IndexStorageBreakdown(
    long MetadataBytes,
    long ExtractedTextBytes,
    long OcrTextBytes,
    long SummariesAndKeywordsBytes,
    long SemanticDataBytes,
    long RelationshipDataBytes,
    long JobHistoryBytes,
    long DiagnosticsBytes,
    long DatabaseBytes,
    long MaximumBytes)
{
    /// <summary>Gets the total physical provider size including managed sidecar files.</summary>
    public long TotalBytes => DatabaseBytes;

    /// <summary>Gets the logical bytes retained for structured media evidence.</summary>
    public long MediaDerivedDataBytes { get; init; }

    /// <summary>Gets the logical bytes retained for bounded content-intelligence evidence.</summary>
    public long ContentIntelligenceBytes { get; init; }
}

/// <summary>Describes current persistent progress suitable for UI binding.</summary>
public sealed record IndexingProgressSnapshot
{
    /// <summary>Gets the active or most recent run identifier.</summary>
    public string? RunId { get; init; }

    /// <summary>Gets the durable run state.</summary>
    public IndexingRunStatus Status { get; init; } = IndexingRunStatus.Complete;

    /// <summary>Gets the currently active stage.</summary>
    public IndexingStage? CurrentStage { get; init; }

    /// <summary>Gets the display-safe current file name, never required to be an absolute path.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Gets the number of files discovered for the run.</summary>
    public long TotalDiscovered { get; init; }

    /// <summary>Gets the number of terminally processed files.</summary>
    public long Processed { get; init; }

    /// <summary>Gets the number of successfully completed files.</summary>
    public long Completed { get; init; }

    /// <summary>Gets the number of intentionally skipped files.</summary>
    public long Skipped { get; init; }

    /// <summary>Gets the number of permanently failed files.</summary>
    public long Failed { get; init; }

    /// <summary>Gets the number of dependency-waiting files.</summary>
    public long Waiting { get; init; }

    /// <summary>Gets the number of retry-scheduled files.</summary>
    public long RetryScheduled { get; init; }

    /// <summary>Gets the number of files not yet terminal.</summary>
    public long Remaining => Math.Max(0, TotalDiscovered - Processed);

    /// <summary>Gets a bounded overall percentage.</summary>
    public double OverallPercentage =>
        TotalDiscovered <= 0 ? (Status is IndexingRunStatus.Complete ? 100 : 0) : Math.Clamp(Processed * 100d / TotalDiscovered, 0, 100);

    /// <summary>Gets recent processing throughput in files per second.</summary>
    public double FilesPerSecond { get; init; }

    /// <summary>Gets an estimate only after a meaningful sample exists.</summary>
    public TimeSpan? EstimatedRemaining { get; init; }

    /// <summary>Gets the current physical index size.</summary>
    public long IndexSizeBytes { get; init; }

    /// <summary>Gets the configured maximum physical index size.</summary>
    public long MaximumIndexSizeBytes { get; init; }

    /// <summary>Gets progressive Search coverage.</summary>
    public SearchCoverage Coverage { get; init; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>Describes a provider-neutral document available to progressive Search.</summary>
public sealed record ProgressiveSearchDocument
{
    /// <summary>Gets the durable file identifier.</summary>
    public required string FileId { get; init; }

    /// <summary>Gets the local full path needed to open the result.</summary>
    public required string FullPath { get; init; }

    /// <summary>Gets the file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the source-relative path.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Gets the folder-name search text.</summary>
    public required string FolderName { get; init; }

    /// <summary>Gets the filename extension including its leading dot.</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>Gets a stable plain-language file category.</summary>
    public string FileType { get; init; } = string.Empty;

    /// <summary>Gets the durable source identifier.</summary>
    public string? SourceId { get; init; }

    /// <summary>Gets the display-safe source name.</summary>
    public string? SourceName { get; init; }

    /// <summary>Gets the configured source priority.</summary>
    public int SourcePriority { get; init; }

    /// <summary>Gets the observed file size.</summary>
    public long Length { get; init; }

    /// <summary>Gets the observed creation time.</summary>
    public DateTimeOffset? CreationTimeUtc { get; init; }

    /// <summary>Gets the observed modification time.</summary>
    public DateTimeOffset? ModifiedTimeUtc { get; init; }

    /// <summary>Gets the file's effective indexing level.</summary>
    public IndexingLevel? IndexingLevel { get; init; }

    /// <summary>Gets bounded basic metadata search text.</summary>
    public string MetadataText { get; init; } = string.Empty;

    /// <summary>Gets bounded extracted document text.</summary>
    public string? ExtractedText { get; init; }

    /// <summary>Gets bounded OCR text.</summary>
    public string? OcrText { get; init; }

    /// <summary>Gets structured image, audio, or video evidence retained by the provider.</summary>
    public IndexedMediaEvidence? MediaEvidence { get; init; }

    /// <summary>Gets bounded topics, textual entities, and summary provenance retained by local indexing.</summary>
    public IndexedContentIntelligence? ContentIntelligence { get; init; }

    /// <summary>Gets bounded tags or generated keywords.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets bounded generated keywords separately from accepted tags.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Gets a bounded derived summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets one document-level search representation.</summary>
    public IReadOnlyList<float>? SemanticRepresentation { get; init; }

    /// <summary>Gets bounded selected chunks retained by Deep indexing.</summary>
    public IReadOnlyList<string> SelectedChunks { get; init; } = [];

    /// <summary>Gets whether all applicable work is complete.</summary>
    public bool IsFullyIndexed { get; init; }

    /// <summary>Gets whether a retained failed stage is associated with this file.</summary>
    public bool HasIndexingFailure { get; init; }

    /// <summary>
    /// Gets whether this path is an index-forget marker used to suppress compatible
    /// legacy Search entries without exposing it as a result.
    /// </summary>
    public bool IsExcluded { get; init; }
}

/// <summary>Describes a privacy-safe indexing failure for user review.</summary>
public sealed record IndexingFailure(
    string RunId,
    string FileName,
    IndexingStage Stage,
    IndexingFailureCategory Category,
    string? ErrorCode,
    int Attempt,
    DateTimeOffset OccurredAtUtc,
    bool CanRetry);

/// <summary>Describes one explicit maintenance action performed by a provider.</summary>
public sealed record IndexMaintenanceAction(string Code, long ReclaimedBytes, DateTimeOffset PerformedAtUtc);

/// <summary>Contains cleanup and quota-enforcement results.</summary>
public sealed record IndexMaintenanceResult(
    IReadOnlyList<IndexMaintenanceAction> Actions,
    IndexStorageBreakdown Storage,
    bool IsWithinQuota);
