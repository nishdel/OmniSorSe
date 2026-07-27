#pragma warning disable CS1591

using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Application.Plugins;

/// <summary>
/// Finds known exact-version references that must block plugin removal.
/// </summary>
/// <remarks>
/// The inspection spans Workflow Profiles, Sorting Recipes, Watched Folders,
/// saved snapshots, and import/export references available to the host. Failure
/// is conservative: package removal must not erase a version whose usage could
/// not be established safely.
/// </remarks>
public sealed class PluginUsageInspector : IPluginUsageInspector
{
    private readonly IWorkflowLibraryService _workflows;
    private readonly IWatchedFolderManager _watchedFolders;

    public PluginUsageInspector(
        IWorkflowLibraryService workflows,
        IWatchedFolderManager watchedFolders)
    {
        _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
        _watchedFolders = watchedFolders ?? throw new ArgumentNullException(nameof(watchedFolders));
    }

    public async Task<PluginUsage> InspectAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await _workflows.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await _workflows.ListProfilesAsync(
            includeArchived: true,
            cancellationToken).ConfigureAwait(false);
        var recipes = await _workflows.ListRecipesAsync(
            includeArchived: true,
            cancellationToken).ConfigureAwait(false);
        var profileIds = profiles
            .Where(profile => profile.PluginContributions.Any(reference =>
                string.Equals(reference.PluginId, pluginId, StringComparison.Ordinal)))
            .Select(profile => profile.Id)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var recipeIds = recipes
            .Where(recipe => recipe.PluginFieldContributions.Any(reference =>
                string.Equals(reference.PluginId, pluginId, StringComparison.Ordinal)))
            .Select(recipe => recipe.Id)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var referencedProfiles = profileIds.ToHashSet(StringComparer.Ordinal);
        var referencedRecipes = recipeIds.ToHashSet(StringComparer.Ordinal);
        var watched = (await _watchedFolders.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(configuration =>
                referencedProfiles.Contains(
                    WatchedWorkflowUsageInspector.NormalizeLegacyProfileId(configuration.ScanProfileId)) ||
                configuration.SortingRecipeIds.Any(referencedRecipes.Contains) ||
                configuration.SortingRecipeId is { } recipeId &&
                    referencedRecipes.Contains(recipeId))
            .Select(configuration => configuration.Id)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var unresolvedImports = profiles
            .Where(profile =>
                profile.Origin.Kind == WorkflowOriginKind.Imported &&
                profile.PluginContributions.Any(reference =>
                    string.Equals(reference.PluginId, pluginId, StringComparison.Ordinal)))
            .Select(profile => profile.Id)
            .Concat(recipes
                .Where(recipe =>
                    recipe.Origin.Kind == WorkflowOriginKind.Imported &&
                    recipe.PluginFieldContributions.Any(reference =>
                        string.Equals(reference.PluginId, pluginId, StringComparison.Ordinal)))
                .Select(recipe => recipe.Id))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new PluginUsage(
            Array.AsReadOnly(profileIds),
            Array.AsReadOnly(recipeIds),
            Array.AsReadOnly(watched),
            Array.AsReadOnly(unresolvedImports));
    }
}
