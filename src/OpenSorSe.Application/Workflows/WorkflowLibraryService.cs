#pragma warning disable CS1591

using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.Watching;
using OpenSorSe.Core;

namespace OpenSorSe.Application.Workflows;

public sealed class WatchedWorkflowUsageInspector : IWorkflowUsageInspector
{
    private readonly IWatchedFolderConfigurationStore _watchedStore;
    private readonly IResultsCatalogStore? _catalogStore;

    public WatchedWorkflowUsageInspector(
        IWatchedFolderConfigurationStore watchedStore,
        IResultsCatalogStore? catalogStore = null)
    {
        _watchedStore = watchedStore ?? throw new ArgumentNullException(nameof(watchedStore));
        _catalogStore = catalogStore;
    }

    public async Task<WorkflowUsageInfo> InspectAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        IReadOnlyList<WatchedFolderConfiguration> configurations;
        try
        {
            configurations = await _watchedStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            configurations = [];
        }

        var watched = configurations
            .Where(configuration =>
                string.Equals(NormalizeLegacyProfileId(configuration.ScanProfileId), itemId, StringComparison.Ordinal) ||
                string.Equals(configuration.SortingRecipeId, itemId, StringComparison.Ordinal) ||
                configuration.SortingRecipeIds.Contains(itemId, StringComparer.Ordinal))
            .Select(configuration => configuration.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var recentScans = 0;
        if (_catalogStore is not null)
        {
            try
            {
                var summaries = await _catalogStore.ListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var summary in summaries.Take(10))
                {
                    var entry = await _catalogStore.LoadAsync(summary.Id, cancellationToken).ConfigureAwait(false);
                    if (string.Equals(entry?.Snapshot.Workflow?.ProfileId, itemId, StringComparison.Ordinal) ||
                        entry?.Snapshot.Workflow?.Recipes.Any(
                            recipe => string.Equals(recipe.RecipeId, itemId, StringComparison.Ordinal)) == true)
                    {
                        recentScans++;
                    }
                }
            }
            catch (InvalidDataException)
            {
                // Usage remains useful even when the optional saved-scan catalog is unavailable.
            }
        }

        return new WorkflowUsageInfo(
            itemId,
            Array.AsReadOnly(watched),
            [],
            recentScans);
    }

    public static string NormalizeLegacyProfileId(string profileId) =>
        string.Equals(profileId, "default", StringComparison.OrdinalIgnoreCase)
            ? BuiltInWorkflowIds.GeneralDocuments
            : profileId;
}

/// <summary>
/// Owns the in-memory workflow library lifecycle and all user-item mutations.
/// </summary>
/// <remarks>
/// The service serializes mutations, clones records at its boundary, protects
/// canonical built-ins, checks known usage before destructive operations, and
/// delegates durable atomic storage to <see cref="IWorkflowLibraryStore"/>.
/// It configures analysis but has no approval or execution authority.
/// </remarks>
public sealed class WorkflowLibraryService : IWorkflowLibraryService
{
    private readonly IWorkflowLibraryStore _store;
    private readonly IWorkflowValidator _validator;
    private readonly IWorkflowUsageInspector _usageInspector;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _diagnosticGate = new();
    private readonly List<WorkflowDiagnostic> _diagnostics = [];
    private IReadOnlyList<WorkflowProfile> _profiles = [];
    private IReadOnlyList<SortingRecipe> _recipes = [];
    private bool _initialized;

    public WorkflowLibraryService(
        IWorkflowLibraryStore store,
        IWorkflowValidator validator,
        IWorkflowUsageInspector usageInspector,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _usageInspector = usageInspector ?? throw new ArgumentNullException(nameof(usageInspector));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string? RecoveryMessage { get; private set; }
    public string? PreservedCorruptCopyPath { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            _profiles = Array.AsReadOnly(loaded.Profiles.Select(JsonWorkflowLibraryStore.Clone).ToArray());
            _recipes = Array.AsReadOnly(loaded.Recipes.Select(JsonWorkflowLibraryStore.Clone).ToArray());
            RecoveryMessage = loaded.RecoveryMessage;
            PreservedCorruptCopyPath = loaded.PreservedCorruptCopyPath;
            Record(
                loaded.RecoveryMessage is null
                    ? WorkflowDiagnosticKind.Load
                    : WorkflowDiagnosticKind.Recovery,
                loaded.RecoveryMessage ?? "Workflow profiles and sorting recipes loaded.");
            if (loaded.Migrated)
            {
                await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
                Record(WorkflowDiagnosticKind.Migration, "The workflow library was migrated to the current schema.");
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<WorkflowProfile>> ListProfilesAsync(
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return Array.AsReadOnly(BuiltInWorkflowLibrary.Profiles
            .Concat(_profiles)
            .Where(profile => includeArchived || !profile.IsArchived)
            .OrderBy(profile => profile.IsArchived)
            .ThenByDescending(profile => profile.IsBuiltIn)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(JsonWorkflowLibraryStore.Clone)
            .ToArray());
    }

    public async Task<IReadOnlyList<SortingRecipe>> ListRecipesAsync(
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return Array.AsReadOnly(BuiltInWorkflowLibrary.Recipes
            .Concat(_recipes)
            .Where(recipe => includeArchived || !recipe.IsArchived)
            .OrderBy(recipe => recipe.IsArchived)
            .ThenByDescending(recipe => recipe.IsBuiltIn)
            .ThenBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase)
            .Select(JsonWorkflowLibraryStore.Clone)
            .ToArray());
    }

    public async Task<WorkflowProfile?> GetProfileAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = WatchedWorkflowUsageInspector.NormalizeLegacyProfileId(id);
        return (await ListProfilesAsync(true, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(profile => string.Equals(profile.Id, normalized, StringComparison.Ordinal));
    }

    public async Task<SortingRecipe?> GetRecipeAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return (await ListRecipesAsync(true, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(recipe => string.Equals(recipe.Id, id, StringComparison.Ordinal));
    }

    public Task<WorkflowProfile> CreateProfileAsync(
        WorkflowProfile profile,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(profile);
            EnsureUniqueProfile(profile.Id, profile.Name, null);
            var now = UtcNow();
            var created = JsonWorkflowLibraryStore.Clone(profile with
            {
                SchemaVersion = WorkflowLibraryLimits.CurrentProfileSchemaVersion,
                Id = UserId(profile.Id, "profile"),
                Revision = 1,
                Name = profile.Name.Trim(),
                Description = NormalizeDescription(profile.Description),
                CreatedAtUtc = now,
                ModifiedAtUtc = now,
                IsBuiltIn = false,
                IsArchived = false,
                Origin = profile.Origin.Kind == WorkflowOriginKind.Imported
                    ? profile.Origin
                    : new WorkflowProfileOrigin(
                        WorkflowOriginKind.UserCreated,
                        SourceApplicationVersion: ApplicationVersionInfo.Current),
            });
            Validate(created);
            _profiles = Array.AsReadOnly(_profiles.Append(created).ToArray());
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Workflow profile created and validated.", created.Id);
            return JsonWorkflowLibraryStore.Clone(created);
        }, cancellationToken);

    public Task<WorkflowProfile> DuplicateProfileAsync(
        string id,
        string newName,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            var source = await GetProfileCoreAsync(id).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The workflow profile no longer exists.");
            EnsureUniqueProfile(null, newName, null);
            var now = UtcNow();
            var duplicate = JsonWorkflowLibraryStore.Clone(source with
            {
                Id = $"profile:{Guid.NewGuid():N}",
                Revision = 1,
                Name = newName.Trim(),
                CreatedAtUtc = now,
                ModifiedAtUtc = now,
                IsBuiltIn = false,
                IsArchived = false,
                Origin = new WorkflowProfileOrigin(
                    WorkflowOriginKind.Duplicated,
                    source.Id,
                    ApplicationVersionInfo.Current),
            });
            Validate(duplicate);
            _profiles = Array.AsReadOnly(_profiles.Append(duplicate).ToArray());
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Workflow profile duplicated and validated.", duplicate.Id);
            return JsonWorkflowLibraryStore.Clone(duplicate);
        }, cancellationToken);

    public Task<WorkflowProfile> UpdateProfileAsync(
        WorkflowProfile profile,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(profile);
            var index = FindUserProfile(profile.Id);
            var current = _profiles[index];
            EnsureUniqueProfile(profile.Id, profile.Name, profile.Id);
            var updated = JsonWorkflowLibraryStore.Clone(profile with
            {
                SchemaVersion = WorkflowLibraryLimits.CurrentProfileSchemaVersion,
                Id = current.Id,
                Revision = checked(current.Revision + 1),
                Name = profile.Name.Trim(),
                Description = NormalizeDescription(profile.Description),
                CreatedAtUtc = current.CreatedAtUtc,
                ModifiedAtUtc = UtcNow(),
                IsBuiltIn = false,
                Origin = current.Origin,
            });
            Validate(updated);
            var profiles = _profiles.ToArray();
            profiles[index] = updated;
            _profiles = Array.AsReadOnly(profiles);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Workflow profile revision saved.", updated.Id);
            return JsonWorkflowLibraryStore.Clone(updated);
        }, cancellationToken);

    public Task<WorkflowProfile> SetProfileArchivedAsync(
        string id,
        bool archived,
        CancellationToken cancellationToken) =>
        UpdateProfileStateAsync(id, profile => profile with { IsArchived = archived }, cancellationToken);

    public Task<WorkflowProfile> SetProfileEnabledAsync(
        string id,
        bool enabled,
        CancellationToken cancellationToken) =>
        UpdateProfileStateAsync(id, profile => profile with { IsEnabled = enabled }, cancellationToken);

    public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            var index = FindUserProfile(id);
            var usage = await _usageInspector.InspectAsync(id, cancellationToken).ConfigureAwait(false);
            if (usage.IsReferenced)
            {
                throw new InvalidOperationException(
                    "The workflow profile is still assigned to a watched folder or historical dependency.");
            }

            _profiles = Array.AsReadOnly(_profiles.Where((_, candidate) => candidate != index).ToArray());
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Unreferenced workflow profile deleted.", id);
            return true;
        }, cancellationToken);

    public Task<SortingRecipe> CreateRecipeAsync(
        SortingRecipe recipe,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(recipe);
            EnsureUniqueRecipe(recipe.Id, recipe.Name, null);
            var now = UtcNow();
            var created = JsonWorkflowLibraryStore.Clone(recipe with
            {
                SchemaVersion = WorkflowLibraryLimits.CurrentRecipeSchemaVersion,
                Id = UserId(recipe.Id, "recipe"),
                Revision = 1,
                Name = recipe.Name.Trim(),
                Description = NormalizeDescription(recipe.Description),
                CreatedAtUtc = now,
                ModifiedAtUtc = now,
                IsBuiltIn = false,
                IsArchived = false,
                Origin = recipe.Origin.Kind == WorkflowOriginKind.Imported
                    ? recipe.Origin
                    : new WorkflowProfileOrigin(
                        WorkflowOriginKind.UserCreated,
                        SourceApplicationVersion: ApplicationVersionInfo.Current),
            });
            Validate(created);
            _recipes = Array.AsReadOnly(_recipes.Append(created).ToArray());
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Sorting recipe created and validated.", created.Id);
            return JsonWorkflowLibraryStore.Clone(created);
        }, cancellationToken);

    public Task<SortingRecipe> DuplicateRecipeAsync(
        string id,
        string newName,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            var source = await GetRecipeCoreAsync(id).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The sorting recipe no longer exists.");
            EnsureUniqueRecipe(null, newName, null);
            var now = UtcNow();
            var duplicate = JsonWorkflowLibraryStore.Clone(source with
            {
                Id = $"recipe:{Guid.NewGuid():N}",
                Revision = 1,
                Name = newName.Trim(),
                CreatedAtUtc = now,
                ModifiedAtUtc = now,
                IsBuiltIn = false,
                IsArchived = false,
                Origin = new WorkflowProfileOrigin(
                    WorkflowOriginKind.Duplicated,
                    source.Id,
                    ApplicationVersionInfo.Current),
            });
            Validate(duplicate);
            _recipes = Array.AsReadOnly(_recipes.Append(duplicate).ToArray());
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Sorting recipe duplicated and validated.", duplicate.Id);
            return JsonWorkflowLibraryStore.Clone(duplicate);
        }, cancellationToken);

    public Task<SortingRecipe> UpdateRecipeAsync(
        SortingRecipe recipe,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(recipe);
            var index = FindUserRecipe(recipe.Id);
            var current = _recipes[index];
            EnsureUniqueRecipe(recipe.Id, recipe.Name, recipe.Id);
            var updated = JsonWorkflowLibraryStore.Clone(recipe with
            {
                SchemaVersion = WorkflowLibraryLimits.CurrentRecipeSchemaVersion,
                Id = current.Id,
                Revision = checked(current.Revision + 1),
                Name = recipe.Name.Trim(),
                Description = NormalizeDescription(recipe.Description),
                CreatedAtUtc = current.CreatedAtUtc,
                ModifiedAtUtc = UtcNow(),
                IsBuiltIn = false,
                Origin = current.Origin,
            });
            Validate(updated);
            var recipes = _recipes.ToArray();
            recipes[index] = updated;
            _recipes = Array.AsReadOnly(recipes);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Sorting recipe revision saved.", updated.Id);
            return JsonWorkflowLibraryStore.Clone(updated);
        }, cancellationToken);

    public Task<SortingRecipe> SetRecipeArchivedAsync(
        string id,
        bool archived,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            var index = FindUserRecipe(id);
            var current = _recipes[index];
            var updated = current with
            {
                IsArchived = archived,
                Revision = checked(current.Revision + 1),
                ModifiedAtUtc = UtcNow(),
            };
            var recipes = _recipes.ToArray();
            recipes[index] = updated;
            _recipes = Array.AsReadOnly(recipes);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            return JsonWorkflowLibraryStore.Clone(updated);
        }, cancellationToken);

    public Task<bool> DeleteRecipeAsync(string id, CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            var index = FindUserRecipe(id);
            var profileReferences = BuiltInWorkflowLibrary.Profiles
                .Concat(_profiles)
                .Where(profile => profile.SortingRecipeIds.Contains(id, StringComparer.Ordinal))
                .Select(profile => profile.Id)
                .ToArray();
            var usage = await _usageInspector.InspectAsync(id, cancellationToken).ConfigureAwait(false);
            if (profileReferences.Length > 0 || usage.IsReferenced)
            {
                throw new InvalidOperationException(
                    "The sorting recipe is still referenced by a profile, watched folder, or historical dependency.");
            }

            _recipes = Array.AsReadOnly(_recipes.Where((_, candidate) => candidate != index).ToArray());
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            Record(WorkflowDiagnosticKind.Validation, "Unreferenced sorting recipe deleted.", id);
            return true;
        }, cancellationToken);

    public async Task<WorkflowUsageInfo> GetUsageAsync(
        string itemId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var inspected = await _usageInspector.InspectAsync(itemId, cancellationToken).ConfigureAwait(false);
        var profiles = BuiltInWorkflowLibrary.Profiles
            .Concat(_profiles)
            .Where(profile => profile.SortingRecipeIds.Contains(itemId, StringComparer.Ordinal))
            .Select(profile => profile.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return inspected with { ProfileIds = Array.AsReadOnly(profiles) };
    }

    public IReadOnlyList<WorkflowDiagnostic> GetDiagnostics()
    {
        lock (_diagnosticGate)
        {
            return Array.AsReadOnly(_diagnostics
                .OrderByDescending(item => item.TimestampUtc)
                .ToArray());
        }
    }

    public void RecordDiagnostic(
        WorkflowDiagnosticKind kind,
        string summary,
        string? itemId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Record(kind, summary, itemId);
    }

    private Task<WorkflowProfile> UpdateProfileStateAsync(
        string id,
        Func<WorkflowProfile, WorkflowProfile> update,
        CancellationToken cancellationToken) =>
        MutateAsync(async () =>
        {
            var index = FindUserProfile(id);
            var current = _profiles[index];
            var updated = update(current) with
            {
                Revision = checked(current.Revision + 1),
                ModifiedAtUtc = UtcNow(),
            };
            Validate(updated);
            var profiles = _profiles.ToArray();
            profiles[index] = updated;
            _profiles = Array.AsReadOnly(profiles);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            return JsonWorkflowLibraryStore.Clone(updated);
        }, cancellationToken);

    private async Task<T> MutateAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var previousProfiles = _profiles;
        var previousRecipes = _recipes;
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch
        {
            _profiles = previousProfiles;
            _recipes = previousRecipes;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<WorkflowProfile?> GetProfileCoreAsync(string id) =>
        Task.FromResult(BuiltInWorkflowLibrary.Profiles
            .Concat(_profiles)
            .FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.Ordinal)));

    private Task<SortingRecipe?> GetRecipeCoreAsync(string id) =>
        Task.FromResult(BuiltInWorkflowLibrary.Recipes
            .Concat(_recipes)
            .FirstOrDefault(recipe => string.Equals(recipe.Id, id, StringComparison.Ordinal)));

    private int FindUserProfile(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var index = _profiles.ToList().FindIndex(profile => string.Equals(profile.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            throw BuiltInWorkflowLibrary.Profiles.Any(profile => string.Equals(profile.Id, id, StringComparison.Ordinal))
                ? new InvalidOperationException("Canonical built-in profiles cannot be edited, archived, disabled, or deleted. Duplicate the profile first.")
                : new KeyNotFoundException("The workflow profile no longer exists.");
        }

        return index;
    }

    private int FindUserRecipe(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var index = _recipes.ToList().FindIndex(recipe => string.Equals(recipe.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            throw BuiltInWorkflowLibrary.Recipes.Any(recipe => string.Equals(recipe.Id, id, StringComparison.Ordinal))
                ? new InvalidOperationException("Canonical built-in recipes cannot be edited, archived, or deleted. Duplicate the recipe first.")
                : new KeyNotFoundException("The sorting recipe no longer exists.");
        }

        return index;
    }

    private void EnsureUniqueProfile(string? id, string name, string? excludingId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A workflow profile name is required.", nameof(name));
        }

        var all = BuiltInWorkflowLibrary.Profiles.Concat(_profiles).ToArray();
        if (!string.IsNullOrWhiteSpace(id) &&
            all.Any(profile =>
                !string.Equals(profile.Id, excludingId, StringComparison.Ordinal) &&
                string.Equals(profile.Id, id, StringComparison.Ordinal)) ||
            all.Any(profile =>
                !string.Equals(profile.Id, excludingId, StringComparison.Ordinal) &&
                string.Equals(profile.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A workflow profile with the same ID or name already exists.");
        }
    }

    private void EnsureUniqueRecipe(string? id, string name, string? excludingId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A sorting recipe name is required.", nameof(name));
        }

        var all = BuiltInWorkflowLibrary.Recipes.Concat(_recipes).ToArray();
        if (!string.IsNullOrWhiteSpace(id) &&
            all.Any(recipe =>
                !string.Equals(recipe.Id, excludingId, StringComparison.Ordinal) &&
                string.Equals(recipe.Id, id, StringComparison.Ordinal)) ||
            all.Any(recipe =>
                !string.Equals(recipe.Id, excludingId, StringComparison.Ordinal) &&
                string.Equals(recipe.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A sorting recipe with the same ID or name already exists.");
        }
    }

    private void Validate(WorkflowProfile profile)
    {
        var result = _validator.ValidateProfile(
            profile,
            BuiltInWorkflowLibrary.Recipes.Concat(_recipes).ToArray());
        ThrowIfInvalid(result);
    }

    private void Validate(SortingRecipe recipe) => ThrowIfInvalid(_validator.ValidateRecipe(recipe));

    private static void ThrowIfInvalid(WorkflowValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new ArgumentException(string.Join(
                " ",
                result.Issues.Where(issue => issue.IsBlocking).Select(issue => issue.Message)));
        }
    }

    private Task SaveCoreAsync(CancellationToken cancellationToken) =>
        _store.SaveAsync(_profiles, _recipes, cancellationToken);

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow().ToUniversalTime();

    private static string UserId(string? requested, string prefix)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return $"{prefix}:{Guid.NewGuid():N}";
        }

        var normalized = requested.Trim();
        if (normalized.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("User-created workflow IDs cannot use the reserved built-in prefix.");
        }

        return normalized;
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Record(WorkflowDiagnosticKind kind, string summary, string? itemId = null)
    {
        lock (_diagnosticGate)
        {
            _diagnostics.Add(new WorkflowDiagnostic(UtcNow(), kind, summary, itemId));
            while (_diagnostics.Count > WorkflowLibraryLimits.MaximumDiagnostics)
            {
                _diagnostics.RemoveAt(0);
            }
        }
    }
}
