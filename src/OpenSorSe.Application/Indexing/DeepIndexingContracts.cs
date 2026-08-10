using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Indexing;

/// <summary>Provides durable indexing storage without exposing provider-specific APIs.</summary>
public interface IDeepIndexStore : IAsyncDisposable
{
    /// <summary>Initializes and validates the current schema atomically.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Requeues work that was running when the previous process ended.</summary>
    Task<int> RecoverInterruptedWorkAsync(DateTimeOffset recoveredAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Returns active or suspended runs that must be resumed rather than replaced.</summary>
    Task<IReadOnlyList<ResumableIndexingRun>> GetResumableRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or updates a durable source without discarding completed file data.</summary>
    Task UpsertSourceAsync(IndexingSource source, CancellationToken cancellationToken = default);

    /// <summary>Returns configured sources.</summary>
    Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates source priority for future claims.</summary>
    Task SetSourcePriorityAsync(string sourceId, int priority, CancellationToken cancellationToken = default);

    /// <summary>Removes a source and applies provider cleanup without touching source files.</summary>
    Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Starts a durable run and returns its identifier.</summary>
    Task<string> BeginRunAsync(string sourceId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Queues one discovery batch using incremental identity and metadata rules.</summary>
    Task EnqueueDiscoveredFilesAsync(
        string runId,
        IReadOnlyList<IndexingFileObservation> files,
        string processorFingerprint,
        int maximumRetryCount,
        CancellationToken cancellationToken = default);

    /// <summary>Completes source reconciliation and marks missing records as deleted.</summary>
    Task CompleteDiscoveryAsync(
        string runId,
        IReadOnlySet<string> observedRelativePaths,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically claims the next eligible durable stage.</summary>
    Task<IndexingWorkItem?> ClaimNextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically stores bounded output and advances or terminates the durable job.</summary>
    Task SaveStageOutputAsync(
        IndexingWorkItem workItem,
        IndexingStageOutput output,
        IndexingStage? nextStage,
        DateTimeOffset completedAtUtc,
        TimeSpan duration,
        DateTimeOffset? retryAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether compatible content-derived work can be shared safely.</summary>
    Task<IndexingStage?> GetReusableContentThroughStageAsync(
        string contentHash,
        IndexingLevel level,
        string processorFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>Marks compatible intermediate stages complete by reusing shared content.</summary>
    Task ReuseContentAsync(
        IndexingWorkItem workItem,
        string contentHash,
        IndexingStage throughStage,
        IndexingStage nextStage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Durably changes active run state for pause, resume, waiting, or cancellation.</summary>
    Task SetActiveRunsStatusAsync(
        IndexingRunStatus status,
        string? reason,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Resumes only waiting runs whose queued or delayed work is now eligible.</summary>
    Task<int> ResumeEligibleWaitingRunsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Marks one run failed while preserving completed file and stage data.</summary>
    Task MarkRunFailedAsync(
        string runId,
        string reason,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Moves eligible failed, waiting, or cancelled work back to the queue.</summary>
    Task<int> RetryIncompleteAsync(DateTimeOffset queuedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Returns an accurate persistent progress snapshot.</summary>
    Task<IndexingProgressSnapshot> GetProgressAsync(
        long maximumIndexSizeBytes,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns progressive Search coverage.</summary>
    Task<SearchCoverage> GetSearchCoverageAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a provider-neutral storage breakdown.</summary>
    Task<IndexStorageBreakdown> GetStorageBreakdownAsync(
        long maximumIndexSizeBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded provider-neutral documents available to Search.</summary>
    Task<IReadOnlyList<ProgressiveSearchDocument>> GetSearchDocumentsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded Search documents for exact durable file identifiers.</summary>
    Task<IReadOnlyList<ProgressiveSearchDocument>> GetSearchDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);

    /// <summary>Returns bounded absolute paths excluded from Search by durable privacy rules.</summary>
    Task<IReadOnlyList<string>> GetExcludedSearchPathsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>Returns privacy-safe failures for the current or most recent run.</summary>
    Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Prunes expired operational data and safely enforces the configured quota.</summary>
    Task<IndexMaintenanceResult> MaintainAsync(
        DeepIndexingSettings settings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Compacts eligible provider storage.</summary>
    Task CompactAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a consistent provider-managed recovery copy.</summary>
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Preserves unreadable storage as a recovery copy and creates a fresh empty schema.</summary>
    Task<string?> ResetStorageAsync(
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Clears derived index data while preserving registered source configuration.</summary>
    Task RebuildAsync(DateTimeOffset requestedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>Discovers files without requiring a storage-provider dependency.</summary>
public interface IIndexFileDiscovery
{
    /// <summary>Enumerates eligible local files safely and cooperatively.</summary>
    IAsyncEnumerable<IndexingFileObservation> DiscoverAsync(
        IndexingSource source,
        DeepIndexingSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>Processes one application-owned stage and returns bounded output.</summary>
public interface IIndexingStageProcessor
{
    /// <summary>Processes one claimed stage cooperatively.</summary>
    Task<IndexingStageOutput> ProcessAsync(
        IndexingWorkItem workItem,
        DeepIndexingSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates a stable non-secret identity for processor and policy compatibility.</summary>
public interface IIndexingProcessorFingerprint
{
    /// <summary>Creates a fingerprint whose change invalidates incompatible derived work.</summary>
    string CreateProcessorFingerprint(DeepIndexingSettings settings);
}

/// <summary>Supplies provider-neutral progressive Search documents.</summary>
public interface IProgressiveSearchSource
{
    /// <summary>Returns bounded indexed documents currently available to Search.</summary>
    Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns current Search coverage.</summary>
    Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns bounded paths that must suppress compatible Search records.</summary>
    Task<IReadOnlyList<string>> GetExcludedPathsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>
/// Resolves a bounded set of exact durable file identifiers without requiring a
/// full progressive-index scan. Contextual Search providers use this additive
/// contract to materialize only already-authorized expansion targets.
/// </summary>
public interface IProgressiveSearchDocumentLookup
{
    /// <summary>Returns visible Search documents for the supplied distinct file identifiers.</summary>
    Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);
}

/// <summary>Controls durable background indexing from application services and ViewModels.</summary>
public interface IBackgroundIndexingService :
    IProgressiveSearchSource,
    IProgressiveSearchDocumentLookup,
    IDisposable,
    IAsyncDisposable
{
    /// <summary>Raised after durable progress changes.</summary>
    event EventHandler<IndexingProgressSnapshot>? ProgressChanged;

    /// <summary>Initializes storage, recovers interrupted work, and starts configured workers.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Registers and queues a local folder for durable discovery and indexing.</summary>
    Task<string> QueueFolderAsync(
        string rootPath,
        IndexingLevel? level = null,
        bool includeSubfolders = true,
        IReadOnlyList<string>? exclusions = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns configured durable sources.</summary>
    Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Durably pauses active work.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Resumes paused or dependency-ready work.</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests prompt cooperative cancellation and records its reason.</summary>
    Task CancelAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>Retries eligible failed or dependency-waiting work.</summary>
    Task<int> RetryFailedAsync(CancellationToken cancellationToken = default);

    /// <summary>Prioritizes one configured source.</summary>
    Task PrioritizeSourceAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Removes an indexing source without deleting source files.</summary>
    Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Clears derived data and queues registered sources again.</summary>
    Task RebuildAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns accurate persistent progress.</summary>
    Task<IndexingProgressSnapshot> GetProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a provider-neutral index storage breakdown.</summary>
    Task<IndexStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns privacy-safe indexing failures.</summary>
    Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs retention, orphan cleanup, compaction, and quota enforcement.</summary>
    Task<IndexMaintenanceResult> MaintainAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provides optional resource eligibility signals with graceful cross-platform fallback.</summary>
public interface IBackgroundResourceMonitor
{
    /// <summary>Returns whether resource policy currently allows background work.</summary>
    Task<BackgroundResourceEligibility> GetEligibilityAsync(
        DeepIndexingSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes resource-policy eligibility without requiring platform-specific APIs.</summary>
public sealed record BackgroundResourceEligibility(bool MayProcess, string? WaitingReason);

/// <summary>Provides optional AI enrichment without coupling the pipeline to Ollama or another provider.</summary>
public interface IIndexingEnrichmentProvider
{
    /// <summary>Gets a stable provider and model version used for incremental invalidation.</summary>
    string Version { get; }

    /// <summary>Returns whether the optional provider can currently accept local work.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Generates bounded enrichment for locally extracted text.</summary>
    Task<IndexingEnrichmentResult> EnrichAsync(
        string fileName,
        string boundedText,
        CancellationToken cancellationToken = default);
}

/// <summary>Contains bounded optional enrichment output.</summary>
public sealed record IndexingEnrichmentResult(string? Summary, IReadOnlyList<string> Keywords);

/// <summary>Signals a malformed durable index that requires explicit recovery.</summary>
public sealed class DeepIndexCorruptException : IOException
{
    /// <summary>Initializes a corruption exception with an actionable message.</summary>
    public DeepIndexCorruptException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a corruption exception with an actionable message and underlying failure.</summary>
    public DeepIndexCorruptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Signals a durable schema created by a newer unsupported OpenSorSe version.</summary>
public sealed class DeepIndexUnsupportedSchemaException : IOException
{
    /// <summary>Initializes an unsupported-schema exception.</summary>
    public DeepIndexUnsupportedSchemaException(int foundVersion, int supportedVersion)
        : base($"The index schema version {foundVersion} is newer than the supported version {supportedVersion}. Update OpenSorSe or restore a compatible backup.")
    {
        FoundVersion = foundVersion;
        SupportedVersion = supportedVersion;
    }

    /// <summary>Gets the version read from storage.</summary>
    public int FoundVersion { get; }

    /// <summary>Gets the maximum supported version.</summary>
    public int SupportedVersion { get; }
}
