using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Resilience;

/// <summary>Controls reviewed stable-ID conflict handling during logical restore.</summary>
public enum StateRestoreMode
{
    /// <summary>Retains unrelated current records and applies incoming stable-ID records.</summary>
    Merge,
    /// <summary>Replaces bounded libraries while still merging sources to avoid implicit data deletion.</summary>
    Replace,
}

/// <summary>Selects independently reviewed logical state categories.</summary>
/// <param name="Settings">Whether settings are restored.</param>
/// <param name="Sources">Whether registered source definitions are merged.</param>
/// <param name="WatchedFolders">Whether watched-source definitions are restored.</param>
/// <param name="RecipesAndProfiles">Whether organization recipes and workflow profiles are restored.</param>
/// <param name="SavedViews">Whether Saved Views are restored.</param>
/// <param name="SmartTagAuthority">Whether exact-identity user tags and decisions are restored.</param>
/// <param name="RelationshipAuthority">Whether exact-file-pair decisions and authored Smart Collection state are restored.</param>
public sealed record StateRestoreSelection(
    bool Settings = true,
    bool Sources = true,
    bool WatchedFolders = true,
    bool RecipesAndProfiles = true,
    bool SavedViews = true,
    bool SmartTagAuthority = true,
    bool RelationshipAuthority = true);

/// <summary>Describes a validated archive without applying it.</summary>
/// <param name="BackupVersion">Logical archive format.</param>
/// <param name="ApplicationVersion">Creating application version.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
/// <param name="SourceCount">Source-definition count.</param>
/// <param name="WatchedFolderCount">Watched-source count.</param>
/// <param name="ProfileCount">Workflow-profile count.</param>
/// <param name="RecipeCount">Organization-recipe count.</param>
/// <param name="SavedViewCount">Saved View count.</param>
/// <param name="SmartTagAuthorityCount">User-authority record count.</param>
/// <param name="RelationshipAuthorityCount">Manual relationship/pair-decision count.</param>
/// <param name="Conflicts">Bounded stable-ID conflict summaries.</param>
/// <param name="Fingerprint">Integrity fingerprint required at apply time.</param>
public sealed record StateRestorePreview(
    string BackupVersion,
    string ApplicationVersion,
    DateTimeOffset CreatedAtUtc,
    int SourceCount,
    int WatchedFolderCount,
    int ProfileCount,
    int RecipeCount,
    int SavedViewCount,
    int SmartTagAuthorityCount,
    int RelationshipAuthorityCount,
    IReadOnlyList<string> Conflicts,
    string Fingerprint)
{
    /// <summary>Gets authored Smart Collection record and tombstone count.</summary>
    public int SmartCollectionAuthorityCount { get; init; }
}

/// <summary>Reports one explicit restore and its recovery point.</summary>
/// <param name="Applied">Whether the reviewed operation completed.</param>
/// <param name="RestoredCategoryCount">Applied category count.</param>
/// <param name="RestoredSmartTagAuthorityCount">Exact-identity authority records restored.</param>
/// <param name="SkippedSmartTagAuthorityCount">Authority records skipped without guessing identity.</param>
/// <param name="RestoredRelationshipAuthorityCount">Exact-pair relationship records restored.</param>
/// <param name="SkippedRelationshipAuthorityCount">Relationship records skipped without guessing identity.</param>
/// <param name="PreRestoreBackupPath">Recovery archive created before application.</param>
/// <param name="Message">Bounded user-facing result.</param>
public sealed record StateRestoreResult(
    bool Applied,
    int RestoredCategoryCount,
    int RestoredSmartTagAuthorityCount,
    int SkippedSmartTagAuthorityCount,
    int RestoredRelationshipAuthorityCount,
    int SkippedRelationshipAuthorityCount,
    string PreRestoreBackupPath,
    string Message)
{
    /// <summary>Gets restored authored collection metadata, membership, overrides, and tombstones.</summary>
    public int RestoredSmartCollectionAuthorityCount { get; init; }

    /// <summary>Gets collection authority skipped because exact stable identities were unavailable.</summary>
    public int SkippedSmartCollectionAuthorityCount { get; init; }
}

/// <summary>Provides a narrow deterministic fault seam for restore rollback testing.</summary>
public interface IStateRestoreFaultInjector
{
    /// <summary>Runs before one named restore category.</summary>
    Task BeforeCategoryAsync(string category, CancellationToken cancellationToken);
}

/// <summary>Production fault injector that performs no work.</summary>
public sealed class NoOpStateRestoreFaultInjector : IStateRestoreFaultInjector
{
    /// <inheritdoc />
    public Task BeforeCategoryAsync(string category, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Exports and restores bounded logical user state without copying the derived index.</summary>
public interface IStateBackupService
{
    /// <summary>Atomically writes a validated logical archive.</summary>
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default);
    /// <summary>Validates and previews an archive without changing state.</summary>
    Task<StateRestorePreview> PreviewRestoreAsync(string backupPath, CancellationToken cancellationToken = default);
    /// <summary>Applies an unchanged reviewed archive and creates a pre-restore recovery point.</summary>
    Task<StateRestoreResult> RestoreAsync(
        string backupPath,
        string expectedFingerprint,
        StateRestoreMode mode,
        StateRestoreSelection selection,
        CancellationToken cancellationToken = default);
}

/// <summary>Versioned ZIP-based logical state backup with fixed, non-extracting entries.</summary>
public sealed class StateBackupService : IStateBackupService
{
    private const int FormatVersion = 2;
    private const int MinimumReadableFormatVersion = 1;
    private const int SchemaVersion = 6;
    private const int MaximumSources = 1_000;
    private const int MaximumViews = 100;
    private const int MaximumWorkflows = 500;
    private const int MaximumWatchedFolders = 1_000;
    private const int MaximumAuthorityRecords = 100_000;
    private const long MaximumArchiveBytes = 32L * 1024 * 1024;
    private const long MaximumStateBytes = 24L * 1024 * 1024;
    private const string ManifestEntryName = "manifest.json";
    private const string StateEntryName = "state.json";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IConfigurationService _configuration;
    private readonly IDeepIndexStore _index;
    private readonly IApplicationPathProvider _paths;
    private readonly ISavedDiscoveryViewStore _savedViews;
    private readonly ISmartTagStore _smartTags;
    private readonly IRelationshipStore _relationships;
    private readonly IStateRestoreFaultInjector _faults;
    private readonly IWatchedFolderConfigurationStore _watchedFolders;
    private readonly IWorkflowLibraryStore _workflows;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>Initializes the logical backup authority.</summary>
    public StateBackupService(
        IConfigurationService configuration,
        IDeepIndexStore index,
        IApplicationPathProvider paths,
        ISavedDiscoveryViewStore savedViews,
        ISmartTagStore smartTags,
        IRelationshipStore relationships,
        IWatchedFolderConfigurationStore watchedFolders,
        IWorkflowLibraryStore workflows,
        IStateRestoreFaultInjector faults)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _savedViews = savedViews ?? throw new ArgumentNullException(nameof(savedViews));
        _smartTags = smartTags ?? throw new ArgumentNullException(nameof(smartTags));
        _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
        _watchedFolders = watchedFolders ?? throw new ArgumentNullException(nameof(watchedFolders));
        _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
        _faults = faults ?? throw new ArgumentNullException(nameof(faults));
    }

    /// <inheritdoc />
    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var destination = RequireAbsoluteArchivePath(destinationPath);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteArchiveAsync(destination, await CaptureAsync(allowCorruptRecovery: false, cancellationToken).ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StateRestorePreview> PreviewRestoreAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var loaded = await ReadArchiveAsync(RequireAbsoluteArchivePath(backupPath), cancellationToken)
            .ConfigureAwait(false);
        var currentSources = await _index.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SavedDiscoveryView> currentViews;
        var savedViewsCorrupt = false;
        try
        {
            currentViews = await _savedViews.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OpenSorSe.Core.Persistence.AuthoritativeStoreCorruptionException)
        {
            currentViews = [];
            savedViewsCorrupt = true;
        }
        var currentWorkflow = await _workflows.LoadAsync(cancellationToken).ConfigureAwait(false);
        var conflicts = new List<string>();
        if (savedViewsCorrupt)
        {
            conflicts.Add("The current Saved View store is corrupt and preserved; reviewed Replace restore is required for that category.");
        }
        AddConflictSummary(conflicts, "source", currentSources.Select(item => item.Id), loaded.Payload.Sources.Select(item => item.Id));
        AddConflictSummary(conflicts, "Saved View", currentViews.Select(item => item.Id), loaded.Payload.SavedViews.Select(item => item.Id));
        AddConflictSummary(conflicts, "workflow profile", currentWorkflow.Profiles.Select(item => item.Id), loaded.Payload.Profiles.Select(item => item.Id));
        AddConflictSummary(conflicts, "organization recipe", currentWorkflow.Recipes.Select(item => item.Id), loaded.Payload.Recipes.Select(item => item.Id));
        return new StateRestorePreview(
            loaded.Manifest.FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            loaded.Manifest.ApplicationVersion,
            loaded.Manifest.CreatedAtUtc,
            loaded.Payload.Sources.Length,
            loaded.Payload.WatchedFolders.Length,
            loaded.Payload.Profiles.Length,
            loaded.Payload.Recipes.Length,
            loaded.Payload.SavedViews.Length,
            loaded.Payload.SmartTagAuthority.Length,
            loaded.Payload.RelationshipAuthority.Length,
            conflicts.AsReadOnly(),
            loaded.Fingerprint)
        {
            SmartCollectionAuthorityCount = loaded.Payload.SmartCollectionAuthority!.Collections.Count +
                loaded.Payload.SmartCollectionAuthority.ForgottenContextKeys.Count,
        };
    }

    /// <inheritdoc />
    public async Task<StateRestoreResult> RestoreAsync(
        string backupPath,
        string expectedFingerprint,
        StateRestoreMode mode,
        StateRestoreSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var source = RequireAbsoluteArchivePath(backupPath);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await ReadArchiveAsync(source, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(loaded.Fingerprint),
                    Encoding.ASCII.GetBytes(expectedFingerprint)))
            {
                throw new InvalidOperationException("The backup changed after preview. Preview it again before restoring.");
            }

            var previous = await CaptureAsync(allowCorruptRecovery: true, cancellationToken).ConfigureAwait(false);
            var recoveryDirectory = Path.Combine(_paths.Paths.StateDirectory, "state-backups");
            Directory.CreateDirectory(recoveryDirectory);
            var recoveryPath = Path.Combine(
                recoveryDirectory,
                $"pre-restore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.oms-state");
            await WriteArchiveAsync(recoveryPath, previous, cancellationToken).ConfigureAwait(false);
            try
            {
                var applied = await ApplyAsync(
                    loaded.Payload,
                    mode,
                    selection,
                    includeSmartCollectionAuthority: loaded.Manifest.FormatVersion >= 2,
                    injectFaults: true,
                    cancellationToken).ConfigureAwait(false);
                return new StateRestoreResult(
                    true,
                    applied.Categories,
                    applied.TagResult.AppliedCount,
                    applied.TagResult.SkippedCount,
                    applied.RelationshipResult.AppliedCount,
                    applied.RelationshipResult.SkippedCount,
                    recoveryPath,
                    applied.TagResult.SkippedCount + applied.RelationshipResult.SkippedCount + applied.CollectionResult.SkippedCount == 0
                        ? "The reviewed OmniSorSe state was restored. No source files were changed."
                        : $"The reviewed state was restored; {applied.TagResult.SkippedCount} Smart Tag, {applied.RelationshipResult.SkippedCount} relationship, and {applied.CollectionResult.SkippedCount} collection authority records were skipped because exact file identity was unavailable.")
                {
                    RestoredSmartCollectionAuthorityCount = applied.CollectionResult.AppliedCount,
                    SkippedSmartCollectionAuthorityCount = applied.CollectionResult.SkippedCount,
                };
            }
            catch
            {
                try
                {
                    await RollBackAsync(previous, loaded.Payload, selection).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the original failure and the pre-restore archive for explicit recovery.
                }

                throw;
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<StateBackupPayload> CaptureAsync(
        bool allowCorruptRecovery,
        CancellationToken cancellationToken)
    {
        var workflow = await _workflows.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!allowCorruptRecovery &&
            (!string.IsNullOrWhiteSpace(workflow.RecoveryMessage) ||
             !string.IsNullOrWhiteSpace(_configuration.InitializationWarning)))
        {
            throw new InvalidDataException("Owned user state needs recovery before it can be exported without loss.");
        }

        IReadOnlyList<SavedDiscoveryView> views;
        try
        {
            views = await _savedViews.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OpenSorSe.Core.Persistence.AuthoritativeStoreCorruptionException) when (allowCorruptRecovery)
        {
            views = [];
        }

        return new StateBackupPayload(
            _configuration.Current,
            (await _index.GetSourcesAsync(cancellationToken).ConfigureAwait(false)).ToArray(),
            (await _watchedFolders.LoadAsync(cancellationToken).ConfigureAwait(false)).ToArray(),
            workflow.Profiles.ToArray(),
            workflow.Recipes.ToArray(),
            views.ToArray(),
            (await _smartTags.ExportUserAuthorityAsync(MaximumAuthorityRecords, cancellationToken).ConfigureAwait(false)).ToArray(),
            (await _relationships.ExportRelationshipUserAuthorityAsync(MaximumAuthorityRecords, cancellationToken).ConfigureAwait(false)).ToArray(),
            await _relationships.ExportSmartCollectionUserAuthorityAsync(MaximumAuthorityRecords, cancellationToken).ConfigureAwait(false));
    }

    private async Task<(int Categories, SmartTagAuthorityRestoreResult TagResult, RelationshipAuthorityRestoreResult RelationshipResult, SmartCollectionAuthorityRestoreResult CollectionResult)> ApplyAsync(
        StateBackupPayload payload,
        StateRestoreMode mode,
        StateRestoreSelection selection,
        bool includeSmartCollectionAuthority,
        bool injectFaults,
        CancellationToken cancellationToken)
    {
        var categories = 0;
        var tagResult = new SmartTagAuthorityRestoreResult(0, 0);
        var relationshipResult = new RelationshipAuthorityRestoreResult(0, 0);
        var collectionResult = new SmartCollectionAuthorityRestoreResult(0, 0);
        if (selection.Settings)
        {
            await BeforeCategoryAsync("settings", injectFaults, cancellationToken).ConfigureAwait(false);
            await _configuration.SaveAsync(payload.Settings, cancellationToken).ConfigureAwait(false);
            categories++;
        }

        if (selection.RecipesAndProfiles)
        {
            await BeforeCategoryAsync("workflows", injectFaults, cancellationToken).ConfigureAwait(false);
            var workflow = mode == StateRestoreMode.Replace
                ? (payload.Profiles.AsEnumerable(), payload.Recipes.AsEnumerable())
                : await MergeWorkflowsAsync(payload, cancellationToken).ConfigureAwait(false);
            await _workflows.SaveReviewedRecoveryAsync(workflow.Item1.ToArray(), workflow.Item2.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            categories++;
        }

        if (selection.SavedViews)
        {
            await BeforeCategoryAsync("saved-views", injectFaults, cancellationToken).ConfigureAwait(false);
            if (mode == StateRestoreMode.Replace)
            {
                await _savedViews.ReplaceAllReviewedAsync(payload.SavedViews, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                foreach (var view in payload.SavedViews)
                {
                    await _savedViews.SaveAsync(view, cancellationToken).ConfigureAwait(false);
                }
            }

            categories++;
        }

        if (selection.WatchedFolders)
        {
            await BeforeCategoryAsync("watched-folders", injectFaults, cancellationToken).ConfigureAwait(false);
            var watched = mode == StateRestoreMode.Replace
                ? payload.WatchedFolders
                : MergeById(await _watchedFolders.LoadAsync(cancellationToken).ConfigureAwait(false), payload.WatchedFolders, item => item.Id);
            await _watchedFolders.SaveAsync(watched, cancellationToken).ConfigureAwait(false);
            categories++;
        }

        if (selection.SmartTagAuthority)
        {
            await BeforeCategoryAsync("smart-tag-authority", injectFaults, cancellationToken).ConfigureAwait(false);
            tagResult = await _smartTags.RestoreUserAuthorityAsync(
                payload.SmartTagAuthority,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            categories++;
        }

        if (selection.Sources)
        {
            await BeforeCategoryAsync("sources", injectFaults, cancellationToken).ConfigureAwait(false);
            foreach (var source in payload.Sources)
            {
                await _index.UpsertSourceAsync(source, cancellationToken).ConfigureAwait(false);
            }

            categories++;
        }

        // Relationship authority is last: its provider transaction is atomic, so no later
        // category failure can leave pair decisions needing lossy compensation.
        if (selection.RelationshipAuthority)
        {
            await BeforeCategoryAsync("relationship-authority", injectFaults, cancellationToken).ConfigureAwait(false);
            relationshipResult = await _relationships.RestoreRelationshipUserAuthorityAsync(
                payload.RelationshipAuthority,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (includeSmartCollectionAuthority)
            {
                collectionResult = await _relationships.RestoreSmartCollectionUserAuthorityAsync(
                    payload.SmartCollectionAuthority!,
                    mode == StateRestoreMode.Replace,
                    DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
            }
            categories++;
        }

        return (categories, tagResult, relationshipResult, collectionResult);
    }

    private Task BeforeCategoryAsync(string category, bool injectFaults, CancellationToken cancellationToken) =>
        injectFaults ? _faults.BeforeCategoryAsync(category, cancellationToken) : Task.CompletedTask;

    private async Task RollBackAsync(
        StateBackupPayload previous,
        StateBackupPayload attempted,
        StateRestoreSelection selection)
    {
        if (selection.RelationshipAuthority)
        {
            var previousKeys = previous.RelationshipAuthority
                .Select(item => $"{item.FirstFileId}\n{item.SecondFileId}")
                .ToHashSet(StringComparer.Ordinal);
            var added = attempted.RelationshipAuthority
                .Where(item => !previousKeys.Contains($"{item.FirstFileId}\n{item.SecondFileId}"))
                .ToArray();
            await _relationships.RemoveRelationshipUserAuthorityAsync(added, CancellationToken.None).ConfigureAwait(false);
        }

        if (selection.SmartTagAuthority)
        {
            var previousKeys = previous.SmartTagAuthority
                .Select(item => $"{item.FileId}\n{item.TagId}")
                .ToHashSet(StringComparer.Ordinal);
            var added = attempted.SmartTagAuthority
                .Where(item => !previousKeys.Contains($"{item.FileId}\n{item.TagId}"))
                .ToArray();
            foreach (var item in added.Where(item => item.IsUserTag))
            {
                await _smartTags.RemoveTagAsync(item.FileId, item.TagId, DateTimeOffset.UtcNow, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            foreach (var fileId in added.Where(item => !item.IsUserTag)
                         .Select(item => item.FileId)
                         .Distinct(StringComparer.Ordinal))
            {
                await _smartTags.ResetTagDecisionsAsync(fileId, DateTimeOffset.UtcNow, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        if (selection.Sources)
        {
            var previousIds = previous.Sources.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var attemptedIds = attempted.Sources.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var current = await _index.GetSourcesAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var source in current.Where(item => attemptedIds.Contains(item.Id) && !previousIds.Contains(item.Id)))
            {
                await _index.RemoveSourceAsync(source.Id, CancellationToken.None).ConfigureAwait(false);
            }
        }

        await ApplyAsync(
                previous,
                StateRestoreMode.Replace,
                selection,
                includeSmartCollectionAuthority: true,
                injectFaults: false,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task<(IEnumerable<WorkflowProfile>, IEnumerable<SortingRecipe>)> MergeWorkflowsAsync(
        StateBackupPayload payload,
        CancellationToken cancellationToken)
    {
        var current = await _workflows.LoadAsync(cancellationToken).ConfigureAwait(false);
        return (
            MergeById(current.Profiles, payload.Profiles, item => item.Id),
            MergeById(current.Recipes, payload.Recipes, item => item.Id));
    }

    private static T[] MergeById<T>(IEnumerable<T> current, IEnumerable<T> incoming, Func<T, string> id)
    {
        var result = current.ToDictionary(id, StringComparer.Ordinal);
        foreach (var item in incoming)
        {
            result[id(item)] = item;
        }

        return result.Values.OrderBy(id, StringComparer.Ordinal).ToArray();
    }

    private static void AddConflictSummary(
        ICollection<string> conflicts,
        string category,
        IEnumerable<string> current,
        IEnumerable<string> incoming)
    {
        var overlap = current.Intersect(incoming, StringComparer.Ordinal).Count();
        if (overlap > 0)
        {
            conflicts.Add($"{overlap} {category} stable-ID conflict(s); the reviewed backup wins in Merge mode.");
        }
    }

    private static async Task WriteArchiveAsync(
        string destination,
        StateBackupPayload payload,
        CancellationToken cancellationToken)
    {
        ValidatePayload(payload);
        var stateBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (stateBytes.LongLength > MaximumStateBytes)
        {
            throw new InvalidDataException("The logical state export exceeds its supported bound.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(stateBytes));
        var manifest = new StateBackupManifest(
            FormatVersion,
            ApplicationVersionInfo.Current,
            ApplicationVersionInfo.SourceRevision,
            ApplicationVersionInfo.BuildConfiguration,
            SchemaVersion,
            DateTimeOffset.UtcNow,
            hash);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteEntryAsync(archive, ManifestEntryName, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken)
                    .ConfigureAwait(false);
                await WriteEntryAsync(archive, StateEntryName, stateBytes, cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporary).Length > MaximumArchiveBytes)
            {
                throw new InvalidDataException("The logical state archive exceeds its supported bound.");
            }

            File.Move(temporary, destination, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await target.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<LoadedBackup> ReadArchiveAsync(string source, CancellationToken cancellationToken)
    {
        var file = new FileInfo(source);
        if (!file.Exists || file.Length is < 1 or > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The selected OmniSorSe state archive is missing or exceeds its supported bound.");
        }

        await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count != 2 ||
            archive.Entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            archive.Entries.Any(entry => entry.FullName is not (ManifestEntryName or StateEntryName)))
        {
            throw new InvalidDataException("The state archive contains unexpected, duplicate, or unsafe entries.");
        }

        var manifestBytes = await ReadEntryAsync(archive.GetEntry(ManifestEntryName)!, 64 * 1024, cancellationToken).ConfigureAwait(false);
        var stateBytes = await ReadEntryAsync(archive.GetEntry(StateEntryName)!, MaximumStateBytes, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<StateBackupManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The state archive manifest is missing.");
        if (manifest.FormatVersion is < MinimumReadableFormatVersion or > FormatVersion || manifest.SchemaVersion != SchemaVersion ||
            string.IsNullOrWhiteSpace(manifest.ApplicationVersion) || manifest.ApplicationVersion.Length > 64 ||
            string.IsNullOrWhiteSpace(manifest.SourceRevision) || manifest.SourceRevision.Length > 64 ||
            string.IsNullOrWhiteSpace(manifest.BuildConfiguration) || manifest.BuildConfiguration.Length > 32 ||
            manifest.CreatedAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(manifest.StateSha256) || manifest.StateSha256.Length != 64 ||
            manifest.StateSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("The state archive uses an unsupported newer or incompatible format.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(stateBytes));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(manifest.StateSha256)))
        {
            throw new InvalidDataException("The state archive failed integrity validation.");
        }

        var payload = JsonSerializer.Deserialize<StateBackupPayload>(stateBytes, JsonOptions)
            ?? throw new InvalidDataException("The state archive payload is missing.");
        payload = payload with
        {
            SmartCollectionAuthority = payload.SmartCollectionAuthority ?? new SmartCollectionAuthorityBundle([], []),
        };
        ValidatePayload(payload);
        return new LoadedBackup(manifest, payload, hash);
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length is < 1 || entry.Length > maximumBytes)
        {
            throw new InvalidDataException("A state archive entry exceeds its supported bound.");
        }

        await using var source = entry.Open();
        using var target = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (target.Length + read > maximumBytes)
            {
                throw new InvalidDataException("A state archive entry expanded beyond its supported bound.");
            }

            target.Write(buffer, 0, read);
        }

        return target.ToArray();
    }

    private static void ValidatePayload(StateBackupPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Settings is null ||
            payload.Sources is null || payload.Sources.Length > MaximumSources ||
            payload.WatchedFolders is null || payload.WatchedFolders.Length > MaximumWatchedFolders ||
            payload.Profiles is null || payload.Profiles.Length > MaximumWorkflows ||
            payload.Recipes is null || payload.Recipes.Length > MaximumWorkflows ||
            payload.SavedViews is null || payload.SavedViews.Length > MaximumViews ||
            payload.SmartTagAuthority is null || payload.SmartTagAuthority.Length > MaximumAuthorityRecords ||
            payload.RelationshipAuthority is null || payload.RelationshipAuthority.Length > MaximumAuthorityRecords ||
            payload.SmartCollectionAuthority is null ||
            payload.SmartCollectionAuthority.Collections.Count > MaximumAuthorityRecords ||
            payload.SmartCollectionAuthority.ForgottenContextKeys.Count > MaximumAuthorityRecords)
        {
            throw new InvalidDataException("The state archive payload exceeds a supported category bound.");
        }

        try
        {
            payload.Settings.Validate();
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The state archive contains invalid application settings.", exception);
        }

        if (payload.Sources.Any(source => source is null || !Path.IsPathFullyQualified(source.RootPath)) ||
            payload.Sources.Select(source => source.Id).Distinct(StringComparer.Ordinal).Count() != payload.Sources.Length ||
            payload.SavedViews.Select(view => view.Id).Distinct(StringComparer.Ordinal).Count() != payload.SavedViews.Length ||
            payload.SmartTagAuthority.Any(item => item is null) ||
            payload.SmartTagAuthority
                .Select(item => $"{item.FileId}\n{item.TagId}")
                .Distinct(StringComparer.Ordinal)
                .Count() != payload.SmartTagAuthority.Length ||
            payload.SmartTagAuthority
                .Where(item => item.IsUserTag)
                .GroupBy(item => item.FileId, StringComparer.Ordinal)
                .Any(group => group.Count() > SmartTagLimits.MaximumUserTagsPerFile) ||
            payload.SmartTagAuthority.Any(item =>
                string.IsNullOrWhiteSpace(item.FileId) || item.FileId.Length > 256 ||
                string.IsNullOrWhiteSpace(item.SourceId) || item.SourceId.Length > 256 ||
                string.IsNullOrWhiteSpace(item.TagId) || item.TagId.Length > 256 ||
                string.IsNullOrWhiteSpace(item.DisplayName) || item.DisplayName.Length > SmartTagLimits.MaximumDisplayNameCharacters ||
                item.DisplayName.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(item.RelativePath) || item.RelativePath.Length > 4096 ||
                Path.IsPathFullyQualified(item.RelativePath) ||
                item.IsUserTag && (item.Type != SmartTagType.UserTag || item.Decision != SmartTagDecision.Accepted) ||
                !item.IsUserTag &&
                    (item.Type is not (SmartTagType.Theme or SmartTagType.DocumentType) ||
                     item.Decision is not (SmartTagDecision.Accepted or SmartTagDecision.Rejected))) ||
            payload.RelationshipAuthority.Any(item => item is null) ||
            payload.RelationshipAuthority
                .Select(item => $"{item.FirstFileId}\n{item.SecondFileId}")
                .Distinct(StringComparer.Ordinal).Count() != payload.RelationshipAuthority.Length ||
            payload.RelationshipAuthority.Any(item =>
                string.IsNullOrWhiteSpace(item.FirstFileId) || item.FirstFileId.Length > 256 ||
                string.IsNullOrWhiteSpace(item.SecondFileId) || item.SecondFileId.Length > 256 ||
                string.CompareOrdinal(item.FirstFileId, item.SecondFileId) >= 0 ||
                item.Decision == RelationshipDecision.None || !Enum.IsDefined(item.Decision) ||
                !Enum.IsDefined(item.Type) ||
                item.CustomType?.Length > 64 || item.CustomType?.Any(char.IsControl) == true ||
                item.Type == RelationshipType.Custom && string.IsNullOrWhiteSpace(item.CustomType)) ||
            payload.SmartCollectionAuthority.Collections.Any(item => item is null) ||
            payload.SmartCollectionAuthority.Collections
                .Select(item => item.CollectionId)
                .Distinct(StringComparer.Ordinal).Count() != payload.SmartCollectionAuthority.Collections.Count ||
            payload.SmartCollectionAuthority.Collections.Any(item =>
                string.IsNullOrWhiteSpace(item.CollectionId) || item.CollectionId.Length > 256 ||
                item.ContextKey?.Length > 256 ||
                string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > RelationshipLimits.MaximumCollectionTitleCharacters ||
                item.Description.Length > 512 || item.RelationshipSummary.Length > 512 ||
                !Enum.IsDefined(item.ContextType) || !Enum.IsDefined(item.CreationSource) ||
                item.CreationSource == SmartCollectionCreationSource.Automatic && string.IsNullOrWhiteSpace(item.ContextKey) ||
                item.ManualMemberFileIds is null || item.ExcludedMemberFileIds is null ||
                item.ManualMemberFileIds.Count > RelationshipLimits.MaximumCollectionMembers ||
                item.ExcludedMemberFileIds.Count > RelationshipLimits.MaximumCollectionMembers ||
                item.ManualMemberFileIds.Concat(item.ExcludedMemberFileIds)
                    .Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256)) ||
            payload.SmartCollectionAuthority.ForgottenContextKeys
                .Distinct(StringComparer.Ordinal).Count() != payload.SmartCollectionAuthority.ForgottenContextKeys.Count ||
            payload.SmartCollectionAuthority.ForgottenContextKeys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 256))
        {
            throw new InvalidDataException("The state archive contains invalid or duplicate authority records.");
        }
    }

    private static string RequireAbsoluteArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute state archive path is required.", nameof(path));
        }

        return Path.GetFullPath(path);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            MaxDepth = 32,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record StateBackupManifest(
        int FormatVersion,
        string ApplicationVersion,
        string SourceRevision,
        string BuildConfiguration,
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        string StateSha256);

    private sealed record StateBackupPayload(
        ApplicationSettings Settings,
        IndexingSource[] Sources,
        WatchedFolderConfiguration[] WatchedFolders,
        WorkflowProfile[] Profiles,
        SortingRecipe[] Recipes,
        SavedDiscoveryView[] SavedViews,
        SmartTagUserAuthority[] SmartTagAuthority,
        RelationshipUserAuthority[] RelationshipAuthority,
        SmartCollectionAuthorityBundle? SmartCollectionAuthority = null);

    private sealed record LoadedBackup(StateBackupManifest Manifest, StateBackupPayload Payload, string Fingerprint);
}
