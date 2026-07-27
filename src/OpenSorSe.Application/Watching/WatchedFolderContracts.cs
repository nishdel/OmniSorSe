#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Executor.Models;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Watching;

public static class WatchedFolderLimits
{
    public const int CurrentConfigurationSchemaVersion = 3;
    public const int CurrentCatalogueSchemaVersion = 2;
    public const int CurrentActivitySchemaVersion = 1;
    public const int MaximumConfigurations = 64;
    public const int MaximumIgnoreRules = 256;
    public const int MaximumPatternLength = 512;
    public const int MaximumPathLength = 32_767;
    public const int MaximumActivityEntries = 1_000;
    public const int MaximumRecentChanges = 250;
    public const int MaximumCatalogueFiles = 250_000;
    public const int MaximumQueuedBatches = 256;
    public const long MaximumConfigurationBytes = 4L * 1024 * 1024;
    public const long MaximumCatalogueBytes = 256L * 1024 * 1024;
    public const long MaximumActivityBytes = 16L * 1024 * 1024;
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MinimumQuietPeriod = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MaximumQuietPeriod = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultStabilityObservation = TimeSpan.FromMilliseconds(350);
}

public enum WatchedFolderStatus
{
    Paused,
    Starting,
    Watching,
    Debouncing,
    Queued,
    Processing,
    Busy,
    Unavailable,
    Inaccessible,
    ProfileUnavailable,
    ReconciliationRequired,
    Error,
}

public enum WatchedFolderNotificationLevel
{
    None,
    ErrorsOnly,
    Summaries,
}

public enum WatchedPathChangeKind
{
    FileCreated,
    FileModified,
    FileRenamed,
    FileMoved,
    FileDeleted,
    DirectoryCreated,
    DirectoryRenamed,
    DirectoryMoved,
    DirectoryDeleted,
    Overflow,
    Unknown,
}

public enum WatchedScanReason
{
    WatcherBatch,
    UserIncrementalScan,
    UserFullReconciliation,
    StartupOfflineReconciliation,
    ResumeReconciliation,
    OverflowRecovery,
    ReconnectReconciliation,
    ConfigurationChangedReconciliation,
    OpenSorSeExecution,
    AiRetry,
}

public enum WatchedActivityKind
{
    WatcherStarted,
    WatcherPaused,
    WatcherResumed,
    FolderUnavailable,
    FolderAvailableAgain,
    ChangeBatchDetected,
    IncrementalScanStarted,
    IncrementalScanCompleted,
    ReconciliationStarted,
    ReconciliationCompleted,
    WatcherOverflow,
    ProcessingDeferred,
    AiAnalysisAttempted,
    ChangePlanCreated,
    ManualScanRequested,
    ConfigurationChanged,
    WatcherDisposed,
    Error,
}

public enum WatchedItemReprocessReason
{
    Discovered,
    MetadataChanged,
    ContentChanged,
    RenamedOrMoved,
    DeletedExternally,
    Reconciliation,
    OpenSorSeExecution,
    DeferredUntilStable,
}

public enum WatchedAiAnalysisState
{
    NotRequested,
    Pending,
    Completed,
    Failed,
}

public sealed record WatchedFolderNotificationPreferences(
    WatchedFolderNotificationLevel Level = WatchedFolderNotificationLevel.Summaries,
    bool NotifyWhenPlanReady = true,
    bool NotifyWhenUnavailable = true);

public sealed record WatchedFolderConfiguration(
    string Id,
    string FolderPath,
    string DisplayName,
    bool IsEnabled,
    bool IncludeSubfolders,
    IReadOnlyList<string> IgnoredPaths,
    IReadOnlyList<string> IgnorePatterns,
    string ScanProfileId,
    string? SortingRecipeId,
    bool DeterministicAnalysisEnabled,
    bool AiAnalysisEnabled,
    WatchedFolderNotificationPreferences Notifications,
    TimeSpan QuietPeriod,
    DateTimeOffset? LastSuccessfulScanUtc,
    DateTimeOffset? LastDetectedChangeUtc,
    WatchedFolderStatus Status,
    string CatalogueId)
{
    public long MaximumFileSizeBytes { get; init; } = 1024L * 1024 * 1024;
    public bool IgnoreHiddenFiles { get; init; } = true;
    public DateTimeOffset? LastReconciliationUtc { get; init; }
    public string? LatestSummary { get; init; }
    public string? LastError { get; init; }
    public int QueuedChangeCount { get; init; }
    public int PendingChangePlanCount { get; init; }
    public IReadOnlyList<string> SortingRecipeIds { get; init; } = [];
    public WorkflowProfileOverride? ProfileOverride { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ResolvedWorkflowConfiguration? EffectiveWorkflow { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public WatchedScanReason? RuntimeScanReason { get; init; }
}

public sealed record WatchedFolderCreateRequest(
    string FolderPath,
    string? DisplayName = null,
    bool IncludeSubfolders = true,
    IReadOnlyList<string>? IgnoredPaths = null,
    IReadOnlyList<string>? IgnorePatterns = null,
    string ScanProfileId = "default",
    string? SortingRecipeId = null,
    bool DeterministicAnalysisEnabled = true,
    bool AiAnalysisEnabled = false,
    WatchedFolderNotificationPreferences? Notifications = null,
    TimeSpan? QuietPeriod = null,
    long MaximumFileSizeBytes = 1024L * 1024 * 1024,
    bool IgnoreHiddenFiles = true)
{
    public IReadOnlyList<string> SortingRecipeIds { get; init; } = [];
    public WorkflowProfileOverride? ProfileOverride { get; init; }
}

public sealed record WatchedFolderUpdateRequest(
    string DisplayName,
    bool IncludeSubfolders,
    IReadOnlyList<string> IgnoredPaths,
    IReadOnlyList<string> IgnorePatterns,
    string ScanProfileId,
    string? SortingRecipeId,
    bool DeterministicAnalysisEnabled,
    bool AiAnalysisEnabled,
    WatchedFolderNotificationPreferences Notifications,
    TimeSpan QuietPeriod,
    long MaximumFileSizeBytes,
    bool IgnoreHiddenFiles)
{
    public IReadOnlyList<string> SortingRecipeIds { get; init; } = [];
    public WorkflowProfileOverride? ProfileOverride { get; init; }
}

public sealed record WatchedFolderHint(
    string ConfigurationId,
    WatchedPathChangeKind Kind,
    string Path,
    string? OldPath,
    DateTimeOffset DetectedAtUtc,
    bool IsDirectory = false);

public sealed record WatchedChangeBatch(
    string BatchId,
    string ConfigurationId,
    WatchedScanReason Reason,
    DateTimeOffset FirstDetectedAtUtc,
    DateTimeOffset LastDetectedAtUtc,
    IReadOnlyList<WatchedFolderHint> Hints,
    bool RequiresFullReconciliation,
    bool SuppressSuggestions = false);

public sealed record WatchedFileState(
    string StableId,
    string FullPath,
    long SizeInBytes,
    DateTimeOffset CreationTimeUtc,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes,
    string? ContentHash,
    FileCategory? Category,
    DuplicateStatus DuplicateStatus,
    string? DuplicateGroupId,
    DateTimeOffset AnalysedAtUtc,
    WatchedItemReprocessReason LastReprocessReason)
{
    public WatchedAiAnalysisState AiAnalysisState { get; init; } = WatchedAiAnalysisState.NotRequested;
    public DateTimeOffset? AiLastAttemptUtc { get; init; }
}

public sealed record WatchedDirectoryState(
    string FullPath,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes);

public sealed record WatchedFolderCatalogue(
    int SchemaVersion,
    string CatalogueId,
    string ConfigurationId,
    string RootPath,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<WatchedFileState> Files,
    IReadOnlyList<WatchedDirectoryState> Directories,
    DateTimeOffset? LastReconciliationUtc,
    bool ReconciliationRequired)
{
    public WorkflowConfigurationSnapshot? Workflow { get; init; }
}

public sealed record WatchedActivityEntry(
    string Id,
    string ConfigurationId,
    WatchedActivityKind Kind,
    DateTimeOffset TimestampUtc,
    string Summary,
    string? BatchId = null,
    int ItemCount = 0,
    string? Detail = null);

public sealed record WatchedChangeSummary(
    int Added,
    int Updated,
    int RenamedOrMoved,
    int Removed,
    int Deferred,
    int Ignored,
    int Unresolved)
{
    public int ChangedCount => Added + Updated + RenamedOrMoved + Removed;
    public bool IsComplete => Unresolved == 0;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Added > 0)
        {
            parts.Add($"{Added} new");
        }

        if (Updated > 0)
        {
            parts.Add($"{Updated} updated");
        }

        if (RenamedOrMoved > 0)
        {
            parts.Add($"{RenamedOrMoved} renamed or moved");
        }

        if (Removed > 0)
        {
            parts.Add($"{Removed} removed externally");
        }

        if (Deferred > 0)
        {
            parts.Add($"{Deferred} deferred");
        }

        if (Unresolved > 0)
        {
            parts.Add($"{Unresolved} unresolved");
        }

        return parts.Count == 0 ? "No catalogue changes detected." : string.Join(", ", parts) + ".";
    }
}

public sealed record WatchedFolderProcessResult(
    string ConfigurationId,
    string BatchId,
    WatchedScanReason Reason,
    WatchedChangeSummary Summary,
    WatchedFolderCatalogue Catalogue,
    IReadOnlyList<ChangePlan> CreatedChangePlans,
    bool AiAttempted,
    bool AiFailed,
    IReadOnlyList<string> Warnings);

public sealed record WatchedFolderRuntimeSnapshot(
    WatchedFolderConfiguration Configuration,
    IReadOnlyList<WatchedActivityEntry> RecentActivity);

/// <summary>Persists opt-in watched-root configuration independently of catalogue state.</summary>
public interface IWatchedFolderConfigurationStore
{
    Task<IReadOnlyList<WatchedFolderConfiguration>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<WatchedFolderConfiguration> configurations, CancellationToken cancellationToken);
}

/// <summary>Persists reconciled per-root filesystem and analysis state.</summary>
public interface IWatchedFolderCatalogueStore
{
    Task<WatchedFolderCatalogue?> GetAsync(string catalogueId, CancellationToken cancellationToken);
    Task UpsertAsync(WatchedFolderCatalogue catalogue, CancellationToken cancellationToken);
}

/// <summary>Persists bounded grouped watcher activity rather than raw operating-system events.</summary>
public interface IWatchedActivityStore
{
    Task<IReadOnlyList<WatchedActivityEntry>> ListAsync(string? configurationId, int maximumCount, CancellationToken cancellationToken);
    Task AppendAsync(WatchedActivityEntry activity, CancellationToken cancellationToken);
}

/// <summary>Owns validated watched-root configuration lifecycle and runtime status updates.</summary>
public interface IWatchedFolderManager
{
    event EventHandler? ConfigurationsChanged;
    Task<IReadOnlyList<WatchedFolderConfiguration>> ListAsync(CancellationToken cancellationToken);
    Task<WatchedFolderConfiguration> AddAsync(WatchedFolderCreateRequest request, CancellationToken cancellationToken);
    Task<WatchedFolderConfiguration> UpdateAsync(string id, WatchedFolderUpdateRequest request, CancellationToken cancellationToken);
    Task<WatchedFolderConfiguration> PauseAsync(string id, CancellationToken cancellationToken);
    Task<WatchedFolderConfiguration> ResumeAsync(string id, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);
    Task<WatchedFolderConfiguration> SetRuntimeStateAsync(
        string id,
        Func<WatchedFolderConfiguration, WatchedFolderConfiguration> update,
        CancellationToken cancellationToken);
}

/// <summary>Adapts operating-system notifications into untrusted watcher hints.</summary>
public interface IWatchedFolderEventSource : IDisposable
{
    event EventHandler<WatchedFolderHint>? HintReceived;
    event EventHandler<Exception>? Error;
    string ConfigurationId { get; }
    void Start();
    void Stop();
}

public interface IWatchedFolderEventSourceFactory
{
    IWatchedFolderEventSource Create(WatchedFolderConfiguration configuration);
}

/// <summary>Provides the read-only filesystem probes used to verify watcher hints.</summary>
public interface IWatchedFileSystem
{
    bool DirectoryExists(string path);
    Task<IReadOnlyList<WatchedFileProbe>> EnumerateAsync(
        WatchedFolderConfiguration configuration,
        CancellationToken cancellationToken);
    Task<WatchedFileProbe?> ProbeAsync(string path, CancellationToken cancellationToken);
}

public sealed record WatchedFileProbe(
    string FullPath,
    bool IsDirectory,
    long SizeInBytes,
    DateTimeOffset CreationTimeUtc,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes,
    string StableId);

/// <summary>Observes a file across bounded probes before content processing.</summary>
public interface IFileStabilityChecker
{
    Task<FileStabilityResult> WaitForStableAsync(
        string path,
        TimeSpan observationPeriod,
        int maximumAttempts,
        CancellationToken cancellationToken);
}

public sealed record FileStabilityResult(bool IsStable, int Attempts, string Message, WatchedFileProbe? Probe);

/// <summary>Resolves configured Sorting Recipes into deterministic proposal rules.</summary>
public interface IWatchedSortingRecipeResolver
{
    Task<IReadOnlyList<FileRule>> ResolveAsync(string? recipeId, CancellationToken cancellationToken);

    async Task<IReadOnlyList<FileRule>> ResolveManyAsync(
        IReadOnlyList<string> recipeIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipeIds);
        var rules = new List<FileRule>();
        foreach (var recipeId in recipeIds)
        {
            rules.AddRange(await ResolveAsync(recipeId, cancellationToken).ConfigureAwait(false));
        }

        return Array.AsReadOnly(rules
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(rule => rule.Priority)
                .First())
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id, StringComparer.Ordinal)
            .ToArray());
    }
}

/// <summary>Creates optional non-mutating Change Plans from a verified watched batch.</summary>
public interface IWatchedSuggestionService
{
    Task<WatchedSuggestionResult> CreateSuggestionsAsync(
        WatchedFolderConfiguration configuration,
        ResultsSnapshot snapshot,
        IReadOnlyList<ResultFile> affectedFiles,
        bool suppressSuggestions,
        CancellationToken cancellationToken);
}

public sealed record WatchedSuggestionResult(
    IReadOnlyList<ChangePlan> Plans,
    bool AiAttempted,
    bool AiFailed,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlySet<string> CompletedAiFileIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> FailedAiFileIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>Classifies verified hints caused by a recorded OpenSorSe operation.</summary>
public interface IWatchedExecutionCorrelation
{
    Task<bool> IsOpenSorSeGeneratedAsync(
        WatchedFolderConfiguration configuration,
        WatchedFolderHint hint,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<bool>> ClassifyBatchAsync(
        WatchedFolderConfiguration configuration,
        IReadOnlyList<WatchedFolderHint> hints,
        CancellationToken cancellationToken)
    {
        var results = new bool[hints.Count];
        for (var index = 0; index < hints.Count; index++)
        {
            results[index] = await IsOpenSorSeGeneratedAsync(
                configuration,
                hints[index],
                cancellationToken).ConfigureAwait(false);
        }

        return Array.AsReadOnly(results);
    }
}

/// <summary>Reconciles and analyses one verified watched batch without execution authority.</summary>
public interface IWatchedFolderProcessor
{
    Task<WatchedFolderProcessResult> ProcessAsync(
        WatchedFolderConfiguration configuration,
        WatchedChangeBatch batch,
        CancellationToken cancellationToken);
}

/// <summary>Owns watcher lifetimes, bounded debounce/queueing, reconciliation, and review events.</summary>
public interface IWatchedFolderCoordinator : IAsyncDisposable
{
    event EventHandler<WatchedFolderRuntimeSnapshot>? StateChanged;
    event EventHandler<WatchedActivityEntry>? ActivityPublished;
    Task InitializeAsync(CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
    Task ScanChangesNowAsync(string configurationId, CancellationToken cancellationToken);
    Task ReconcileNowAsync(string configurationId, CancellationToken cancellationToken);
    Task RetryAiAsync(string configurationId, CancellationToken cancellationToken);
}
