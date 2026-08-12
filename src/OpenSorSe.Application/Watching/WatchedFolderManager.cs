#pragma warning disable CS1591

using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Application.Watching;

public sealed class WatchedFolderManager : IWatchedFolderManager
{
    private readonly IWatchedFolderConfigurationStore _store;
    private readonly WatchedFolderPathPolicy _pathPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<WatchedFolderConfiguration>? _cached;

    public WatchedFolderManager(
        IWatchedFolderConfigurationStore store,
        WatchedFolderPathPolicy pathPolicy,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler? ConfigurationsChanged;

    public async Task<IReadOnlyList<WatchedFolderConfiguration>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Clone(await LoadCoreAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WatchedFolderConfiguration> AddAsync(
        WatchedFolderCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = _pathPolicy.CanonicalizeRoot(request.FolderPath);
        ValidateSettings(
            request.DisplayName ?? Path.GetFileName(root),
            request.IgnoredPaths ?? [],
            request.IgnorePatterns ?? [],
            request.ScanProfileId,
            request.SortingRecipeIds,
            request.QuietPeriod ?? WatchedFolderLimits.DefaultQuietPeriod,
            request.MaximumFileSizeBytes);

        WatchedFolderConfiguration created;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configurations = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            EnsureNoOverlap(configurations, root, null);
            if (configurations.Count >= WatchedFolderLimits.MaximumConfigurations)
            {
                throw new InvalidOperationException(
                    $"At most {WatchedFolderLimits.MaximumConfigurations} watched folders are supported.");
            }

            var name = string.IsNullOrWhiteSpace(request.DisplayName)
                ? Path.GetFileName(root)
                : request.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = root;
            }

            created = new WatchedFolderConfiguration(
                $"watch:{Guid.NewGuid():N}",
                root,
                name,
                true,
                request.IncludeSubfolders,
                Array.AsReadOnly((request.IgnoredPaths ?? []).Select(value => value.Trim()).ToArray()),
                Array.AsReadOnly((request.IgnorePatterns ?? []).Select(value => value.Trim()).ToArray()),
                request.ScanProfileId.Trim(),
                string.IsNullOrWhiteSpace(request.SortingRecipeId) ? null : request.SortingRecipeId.Trim(),
                request.DeterministicAnalysisEnabled,
                request.AiAnalysisEnabled,
                request.Notifications ?? new WatchedFolderNotificationPreferences(),
                request.QuietPeriod ?? WatchedFolderLimits.DefaultQuietPeriod,
                null,
                null,
                Directory.Exists(root) ? WatchedFolderStatus.Starting : WatchedFolderStatus.Unavailable,
                $"watched-catalogue:{Guid.NewGuid():N}")
            {
                MaximumFileSizeBytes = request.MaximumFileSizeBytes,
                IgnoreHiddenFiles = request.IgnoreHiddenFiles,
                SortingRecipeIds = Array.AsReadOnly(request.SortingRecipeIds.ToArray()),
                ProfileOverride = request.ProfileOverride,
                LatestSummary = Directory.Exists(root)
                    ? "Watching is starting."
                    : "The folder is currently unavailable; its configuration was retained.",
            };
            configurations.Add(created);
            await SaveCoreAsync(configurations, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        return created;
    }

    public async Task<WatchedFolderConfiguration> UpdateAsync(
        string id,
        WatchedFolderUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(request);
        ValidateSettings(
            request.DisplayName,
            request.IgnoredPaths,
            request.IgnorePatterns,
            request.ScanProfileId,
            request.SortingRecipeIds,
            request.QuietPeriod,
            request.MaximumFileSizeBytes);

        var updated = await MutateAsync(
            id,
            configuration => configuration with
            {
                DisplayName = request.DisplayName.Trim(),
                IncludeSubfolders = request.IncludeSubfolders,
                IgnoredPaths = Array.AsReadOnly(request.IgnoredPaths.Select(value => value.Trim()).ToArray()),
                IgnorePatterns = Array.AsReadOnly(request.IgnorePatterns.Select(value => value.Trim()).ToArray()),
                ScanProfileId = request.ScanProfileId.Trim(),
                SortingRecipeId = string.IsNullOrWhiteSpace(request.SortingRecipeId)
                    ? null
                    : request.SortingRecipeId.Trim(),
                SortingRecipeIds = Array.AsReadOnly(request.SortingRecipeIds
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()),
                ProfileOverride = request.ProfileOverride,
                DeterministicAnalysisEnabled = request.DeterministicAnalysisEnabled,
                AiAnalysisEnabled = request.AiAnalysisEnabled,
                Notifications = request.Notifications,
                QuietPeriod = request.QuietPeriod,
                MaximumFileSizeBytes = request.MaximumFileSizeBytes,
                IgnoreHiddenFiles = request.IgnoreHiddenFiles,
                LatestSummary = "Watched-folder settings were updated.",
            },
            cancellationToken).ConfigureAwait(false);
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    public async Task<WatchedFolderConfiguration> PauseAsync(string id, CancellationToken cancellationToken)
    {
        var paused = await MutateAsync(
            id,
            configuration => configuration with
            {
                IsEnabled = false,
                Status = WatchedFolderStatus.Paused,
                QueuedChangeCount = 0,
                LatestSummary = "Watching is paused. Files and scan history were not changed.",
            },
            cancellationToken).ConfigureAwait(false);
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        return paused;
    }

    public async Task<WatchedFolderConfiguration> ResumeAsync(string id, CancellationToken cancellationToken)
    {
        var resumed = await MutateAsync(
            id,
            configuration => configuration with
            {
                IsEnabled = true,
                Status = Directory.Exists(configuration.FolderPath)
                    ? WatchedFolderStatus.Starting
                    : WatchedFolderStatus.Unavailable,
                LatestSummary = Directory.Exists(configuration.FolderPath)
                    ? "Watching is resuming; changes made while paused will be reconciled."
                    : "The folder remains unavailable; watching will resume when it returns.",
            },
            cancellationToken).ConfigureAwait(false);
        ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        return resumed;
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var removed = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configurations = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            removed = configurations.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                await SaveCoreAsync(configurations, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (removed)
        {
            ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public Task<WatchedFolderConfiguration> SetRuntimeStateAsync(
        string id,
        Func<WatchedFolderConfiguration, WatchedFolderConfiguration> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return MutateAsync(id, update, cancellationToken);
    }

    private async Task<WatchedFolderConfiguration> MutateAsync(
        string id,
        Func<WatchedFolderConfiguration, WatchedFolderConfiguration> update,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configurations = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var index = configurations.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new KeyNotFoundException("The watched-folder configuration no longer exists.");
            }

            var updated = update(configurations[index]) ??
                          throw new InvalidOperationException("The watched-folder update returned no configuration.");
            if (!string.Equals(updated.Id, configurations[index].Id, StringComparison.Ordinal) ||
                !_pathPolicy.IsWithinRoot(configurations[index].FolderPath, updated.FolderPath) ||
                !_pathPolicy.IsWithinRoot(updated.FolderPath, configurations[index].FolderPath))
            {
                throw new InvalidOperationException("A watched-folder update cannot change its stable identity or root path.");
            }

            configurations[index] = updated;
            await SaveCoreAsync(configurations, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<WatchedFolderConfiguration>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_cached is null)
        {
            var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (var first = 0; first < loaded.Count; first++)
                {
                    EnsureNoOverlap(loaded, loaded[first].FolderPath, loaded[first].Id);
                }
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(
                    "The persisted watched-folder configuration contains duplicate or overlapping roots.",
                    exception);
            }

            _cached = Clone(loaded);
        }

        return _cached;
    }

    private async Task SaveCoreAsync(
        IReadOnlyList<WatchedFolderConfiguration> configurations,
        CancellationToken cancellationToken)
    {
        await _store.SaveAsync(configurations, cancellationToken).ConfigureAwait(false);
        _cached = Clone(configurations);
    }

    private void EnsureNoOverlap(
        IReadOnlyList<WatchedFolderConfiguration> configurations,
        string root,
        string? excludingId)
    {
        var conflict = configurations.FirstOrDefault(configuration =>
            !string.Equals(configuration.Id, excludingId, StringComparison.Ordinal) &&
            _pathPolicy.Overlaps(configuration.FolderPath, root));
        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"The watched root overlaps \"{conflict.DisplayName}\". OmniSorSe rejects overlapping roots to prevent duplicate processing.");
        }
    }

    private static void ValidateSettings(
        string? displayName,
        IReadOnlyList<string> ignoredPaths,
        IReadOnlyList<string> ignorePatterns,
        string scanProfileId,
        IReadOnlyList<string> sortingRecipeIds,
        TimeSpan quietPeriod,
        long maximumFileSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(ignoredPaths);
        ArgumentNullException.ThrowIfNull(ignorePatterns);
        ArgumentNullException.ThrowIfNull(sortingRecipeIds);
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Trim().Length > 256 ||
            string.IsNullOrWhiteSpace(scanProfileId) ||
            scanProfileId.Trim().Length > 256 ||
            ignoredPaths.Count > WatchedFolderLimits.MaximumIgnoreRules ||
            ignorePatterns.Count > WatchedFolderLimits.MaximumIgnoreRules ||
            ignoredPaths.Any(path =>
                string.IsNullOrWhiteSpace(path) || path.Length > WatchedFolderLimits.MaximumPathLength) ||
            ignorePatterns.Any(pattern =>
                string.IsNullOrWhiteSpace(pattern) || pattern.Length > WatchedFolderLimits.MaximumPatternLength) ||
            sortingRecipeIds.Count > WorkflowLibraryLimits.MaximumRecipes ||
            sortingRecipeIds.Any(id =>
                string.IsNullOrWhiteSpace(id) ||
                id.Length > WorkflowLibraryLimits.MaximumIdentifierLength) ||
            quietPeriod < WatchedFolderLimits.MinimumQuietPeriod ||
            quietPeriod > WatchedFolderLimits.MaximumQuietPeriod ||
            maximumFileSizeBytes <= 0)
        {
            throw new ArgumentException("The watched-folder settings are invalid or exceed supported bounds.");
        }
    }

    private static IReadOnlyList<WatchedFolderConfiguration> Clone(
        IReadOnlyList<WatchedFolderConfiguration> values) =>
        Array.AsReadOnly(values.Select(value => value with
        {
            IgnoredPaths = Array.AsReadOnly(value.IgnoredPaths.ToArray()),
            IgnorePatterns = Array.AsReadOnly(value.IgnorePatterns.ToArray()),
            SortingRecipeIds = Array.AsReadOnly(value.SortingRecipeIds.ToArray()),
            EffectiveWorkflow = null,
        }).ToArray());
}
