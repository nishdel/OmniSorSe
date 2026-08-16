using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Lifecycle;
using OpenSorSe.Core.Platform;
using OpenSorSe.Executor;

namespace OpenSorSe.Application.Resilience;

/// <summary>Describes one bounded operational-health result.</summary>
public enum OperationalHealthState
{
    /// <summary>All checked authorities are available and consistent.</summary>
    Healthy,
    /// <summary>A non-blocking condition needs user attention.</summary>
    Attention,
    /// <summary>Authoritative mutation or persistence state requires recovery.</summary>
    RecoveryRequired,
    /// <summary>A required authority could not be reached.</summary>
    Unavailable,
    /// <summary>The check could not establish a reliable state.</summary>
    Unknown,
}

/// <summary>Contains a privacy-safe bounded health issue.</summary>
/// <param name="Code">Stable non-sensitive issue code.</param>
/// <param name="State">Issue severity state.</param>
/// <param name="Summary">Short user-facing summary.</param>
/// <param name="Guidance">Explicit safe next step.</param>
public sealed record OperationalHealthIssue(
    string Code,
    OperationalHealthState State,
    string Summary,
    string Guidance);

/// <summary>Contains one non-destructive health snapshot.</summary>
/// <param name="State">Overall state.</param>
/// <param name="CheckedAtUtc">Check timestamp.</param>
/// <param name="Issues">Bounded issues.</param>
/// <param name="SourceCount">Registered-source count.</param>
/// <param name="UnreachableSourceCount">Currently unreachable enabled-source count.</param>
/// <param name="FailedJobCount">Latest failed indexing count.</param>
/// <param name="BackupAvailable">Whether a pre-restore recovery archive is present.</param>
public sealed record OperationalHealthSnapshot(
    OperationalHealthState State,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<OperationalHealthIssue> Issues,
    int SourceCount,
    int UnreachableSourceCount,
    long FailedJobCount,
    bool BackupAvailable);

/// <summary>Runs bounded state and metadata checks without reading indexed document contents.</summary>
public interface IOperationalHealthService
{
    /// <summary>Runs the bounded non-repairing health checks.</summary>
    Task<OperationalHealthSnapshot> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>Coordinates provider and authoritative-store health probes for Home/Settings diagnostics.</summary>
public sealed class OperationalHealthService : IOperationalHealthService
{
    private const long LowSpaceThresholdBytes = 512L * 1024 * 1024;
    private readonly IConfigurationService _configuration;
    private readonly IBackgroundIndexingService _indexing;
    private readonly IDeepIndexHealthProbe _indexHealth;
    private readonly IApplicationPathProvider _paths;
    private readonly IProfileOwnershipState _profileOwnership;
    private readonly IRecoverySafetyState _recovery;
    private readonly IApplicationRunState _runState;
    private readonly ISavedDiscoveryViewStore _savedViews;
    private readonly IWatchedFolderConfigurationStore _watchedFolders;
    private readonly IWorkflowLibraryStore _workflows;

    /// <summary>Initializes the bounded health coordinator.</summary>
    public OperationalHealthService(
        IConfigurationService configuration,
        IBackgroundIndexingService indexing,
        IDeepIndexHealthProbe indexHealth,
        IApplicationPathProvider paths,
        IProfileOwnershipState profileOwnership,
        IRecoverySafetyState recovery,
        IApplicationRunState runState,
        ISavedDiscoveryViewStore savedViews,
        IWatchedFolderConfigurationStore watchedFolders,
        IWorkflowLibraryStore workflows)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        _indexHealth = indexHealth ?? throw new ArgumentNullException(nameof(indexHealth));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _profileOwnership = profileOwnership ?? throw new ArgumentNullException(nameof(profileOwnership));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _runState = runState ?? throw new ArgumentNullException(nameof(runState));
        _savedViews = savedViews ?? throw new ArgumentNullException(nameof(savedViews));
        _watchedFolders = watchedFolders ?? throw new ArgumentNullException(nameof(watchedFolders));
        _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
    }

    /// <inheritdoc />
    public async Task<OperationalHealthSnapshot> CheckAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<OperationalHealthIssue>();
        if (_profileOwnership.Status != ProfileOwnershipStatus.Owned)
        {
            issues.Add(Issue("profile-ownership", OperationalHealthState.RecoveryRequired, "This process does not own the active profile.", "Close competing instances before changing state."));
        }

        if (_recovery.IsMutationBlocked)
        {
            issues.Add(Issue("mutation-recovery", OperationalHealthState.RecoveryRequired, "Authoritative mutation recovery state is corrupted.", _recovery.Message ?? "Review recovery guidance before changing files."));
        }

        if (_runState.PreviousShutdownWasAbnormal)
        {
            issues.Add(Issue("previous-shutdown", OperationalHealthState.Attention, "The prior run did not reach its clean shutdown marker.", "Review indexing and Change Plan recovery status before continuing."));
        }

        if (!string.IsNullOrWhiteSpace(_configuration.InitializationWarning))
        {
            issues.Add(Issue("settings-store", OperationalHealthState.Attention, "Owned settings could not be loaded.", _configuration.InitializationWarning));
        }

        await ProbeStoreAsync("saved-views", "Saved Views", () => _savedViews.ListAsync(cancellationToken), issues).ConfigureAwait(false);
        await ProbeStoreAsync("watched-folders", "Watched-source settings", () => _watchedFolders.LoadAsync(cancellationToken), issues).ConfigureAwait(false);
        try
        {
            var workflow = await _workflows.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(workflow.RecoveryMessage))
            {
                issues.Add(Issue("workflow-library", OperationalHealthState.Attention, "The workflow library needs reviewed recovery.", workflow.RecoveryMessage));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            issues.Add(StoreUnavailable("workflow-library", "Workflow library", exception));
        }

        try
        {
            var sqlite = await _indexHealth.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            if (!sqlite.IsHealthy)
            {
                issues.Add(Issue("sqlite-integrity", OperationalHealthState.RecoveryRequired, "The derived index needs recovery.", sqlite.Message));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            issues.Add(StoreUnavailable("sqlite-integrity", "Derived index", exception));
        }

        IReadOnlyList<IndexingSource> sources = [];
        IndexingProgressSnapshot progress = new();
        try
        {
            sources = await _indexing.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
            progress = await _indexing.GetProgressAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            issues.Add(StoreUnavailable("index-progress", "Index progress", exception));
        }

        var unreachable = sources.Count(source => source.Enabled && !Directory.Exists(source.RootPath));
        if (unreachable > 0)
        {
            issues.Add(Issue("source-reachability", OperationalHealthState.Attention, $"{unreachable} enabled source(s) are currently unavailable.", "Reconnect removable storage or review the registered source."));
        }

        if (progress.Failed > 0)
        {
            issues.Add(Issue("failed-index-jobs", OperationalHealthState.Attention, $"{progress.Failed} indexing item(s) failed in the current or latest run.", "Review local diagnostics and retry only after addressing the failure category."));
        }

        ProbeApplicationStorage(issues);
        var backupDirectory = Path.Combine(_paths.Paths.StateDirectory, "state-backups");
        var backupAvailable = false;
        try
        {
            backupAvailable = Directory.Exists(backupDirectory) &&
                Directory.EnumerateFiles(backupDirectory, "*.oms-state", SearchOption.TopDirectoryOnly).Take(1).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(StoreUnavailable("recovery-backups", "Recovery backup storage", exception));
        }
        var state = HighestState(issues);
        return new OperationalHealthSnapshot(
            state,
            DateTimeOffset.UtcNow,
            issues.AsReadOnly(),
            sources.Count,
            unreachable,
            progress.Failed,
            backupAvailable);
    }

    private void ProbeApplicationStorage(ICollection<OperationalHealthIssue> issues)
    {
        try
        {
            var root = Path.GetPathRoot(_paths.Paths.DataDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace < LowSpaceThresholdBytes)
                {
                    issues.Add(Issue("low-app-data-space", OperationalHealthState.Attention, "Application-data storage is low.", "Free space before indexing, migration, backup, or file operations."));
                }
            }

            var probe = Path.Combine(_paths.Paths.StateDirectory, $".health-write-{Guid.NewGuid():N}.tmp");
            try
            {
                using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                stream.WriteByte(0);
            }
            finally
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(StoreUnavailable("app-data-writable", "Application-data storage", exception));
        }
    }

    private static async Task ProbeStoreAsync<T>(
        string code,
        string name,
        Func<Task<T>> probe,
        ICollection<OperationalHealthIssue> issues)
    {
        try
        {
            _ = await probe().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            issues.Add(StoreUnavailable(code, name, exception));
        }
    }

    private static OperationalHealthIssue StoreUnavailable(string code, string name, Exception exception) =>
        Issue(code, OperationalHealthState.Unavailable, $"{name} is unavailable.", $"Review the preserved state and local diagnostics. Failure category: {exception.GetType().Name}.");

    private static OperationalHealthIssue Issue(string code, OperationalHealthState state, string summary, string guidance) =>
        new(code, state, summary, guidance);

    private static OperationalHealthState HighestState(IEnumerable<OperationalHealthIssue> issues)
    {
        var values = issues.Select(issue => issue.State).ToArray();
        if (values.Contains(OperationalHealthState.RecoveryRequired))
        {
            return OperationalHealthState.RecoveryRequired;
        }

        if (values.Contains(OperationalHealthState.Unavailable))
        {
            return OperationalHealthState.Unavailable;
        }

        return values.Length == 0 ? OperationalHealthState.Healthy : OperationalHealthState.Attention;
    }
}
