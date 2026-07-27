#pragma warning disable CS1591

using OpenSorSe.Application.Content;
using OpenSorSe.Application.Plugins;
using OpenSorSe.Application.Watching;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Rules.Models;

namespace OpenSorSe.Application.Workflows;

/// <summary>
/// Resolves a profile, recipe selection, application settings, and plugin dependencies into one immutable effective configuration.
/// </summary>
/// <remarks>
/// Resolution is conjunctive: application-wide safety switches are ceilings,
/// while profile, watched-root, and one-time settings can only narrow them.
/// Exact plugin-version dependencies must be active and compatible. Missing or
/// unavailable capabilities fail closed; unrelated profiles are never used as
/// a silent fallback.
/// </remarks>
public sealed class WorkflowConfigurationResolver : IWorkflowConfigurationResolver
{
    private readonly IWorkflowLibraryService _library;
    private readonly IConfigurationService _configurationService;
    private readonly IOcrService? _ocrService;
    private readonly IPluginContributionResolver? _pluginResolver;
    private readonly IPluginContributionRegistry? _pluginRegistry;
    private readonly TimeProvider _timeProvider;
    private readonly IPluginDiagnostics? _pluginDiagnostics;

    public WorkflowConfigurationResolver(
        IWorkflowLibraryService library,
        IConfigurationService configurationService,
        IOcrService? ocrService = null,
        IPluginContributionResolver? pluginResolver = null,
        IPluginContributionRegistry? pluginRegistry = null,
        TimeProvider? timeProvider = null,
        IPluginDiagnostics? pluginDiagnostics = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _ocrService = ocrService;
        _pluginResolver = pluginResolver;
        _pluginRegistry = pluginRegistry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pluginDiagnostics = pluginDiagnostics;
    }

    public async Task<WorkflowResolutionResult> ResolveForWatchedFolderAsync(
        WatchedFolderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var warnings = new List<string>();
        var profileId = WatchedWorkflowUsageInspector.NormalizeLegacyProfileId(configuration.ScanProfileId);
        if (!string.Equals(profileId, configuration.ScanProfileId, StringComparison.Ordinal))
        {
            warnings.Add("The v1.2 profile ID \"default\" was explicitly migrated to the General Documents built-in profile.");
        }

        var requestedRecipeIds = configuration.SortingRecipeIds.Count > 0
            ? configuration.SortingRecipeIds
            : string.IsNullOrWhiteSpace(configuration.SortingRecipeId)
                ? null
                : [configuration.SortingRecipeId];
        if (requestedRecipeIds?.Any(id =>
                string.Equals(id, "current", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return Unavailable(
                "Profile unavailable — review configuration. The v1.2 session-only \"current\" recipe must be replaced with a persistent recipe.",
                profileId);
        }

        var folderOverride = MergeOverrides(
            configuration.ProfileOverride,
            new WorkflowProfileOverride(
                MaximumFileSizeBytes: configuration.MaximumFileSizeBytes,
                AiEnabled: configuration.AiAnalysisEnabled));
        var result = await ResolveAsync(
            profileId,
            requestedRecipeIds,
            folderOverride,
            configuration.DeterministicAnalysisEnabled,
            configuration.Id,
            cancellationToken).ConfigureAwait(false);
        return result with
        {
            Warnings = Array.AsReadOnly(warnings.Concat(result.Warnings).Distinct(StringComparer.Ordinal).ToArray()),
        };
    }

    public Task<WorkflowResolutionResult> ResolveForManualScanAsync(
        string profileId,
        WorkflowProfileOverride? oneTimeOverride,
        CancellationToken cancellationToken) =>
        ResolveAsync(
            WatchedWorkflowUsageInspector.NormalizeLegacyProfileId(profileId),
            null,
            oneTimeOverride,
            deterministicAnalysisEnabled: true,
            "manual scan",
            cancellationToken);

    private async Task<WorkflowResolutionResult> ResolveAsync(
        string profileId,
        IReadOnlyList<string>? requestedRecipeIds,
        WorkflowProfileOverride? overrideSettings,
        bool deterministicAnalysisEnabled,
        string resolutionSource,
        CancellationToken cancellationToken)
    {
        await _library.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var profile = await _library.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return Unavailable(
                $"Profile unavailable — review configuration. Profile \"{profileId}\" does not exist.",
                profileId);
        }

        if (profile.SchemaVersion != WorkflowLibraryLimits.CurrentProfileSchemaVersion)
        {
            return Unavailable(
                "Profile unavailable — review configuration. The profile schema is incompatible.",
                profileId);
        }

        if (profile.IsArchived)
        {
            return Unavailable(
                "Profile unavailable — review configuration. The assigned profile is archived.",
                profileId);
        }

        if (!profile.IsEnabled)
        {
            return Unavailable(
                "Profile unavailable — review configuration. The assigned profile is disabled.",
                profileId);
        }

        var scanBehavior = resolutionSource == "manual scan"
            ? profile.FullScan
            : profile.IncrementalScan;
        if (!scanBehavior.Enabled)
        {
            return Unavailable(
                resolutionSource == "manual scan"
                    ? "Profile unavailable — review configuration. The profile does not permit full manual scans."
                    : "Profile unavailable — review configuration. The profile does not permit incremental watched-folder scans.",
                profileId);
        }

        var selectedIds = requestedRecipeIds is null
            ? profile.SortingRecipeIds
            : requestedRecipeIds;
        if (selectedIds.Any(id => !profile.SortingRecipeIds.Contains(id, StringComparer.Ordinal)))
        {
            return Unavailable(
                "Profile unavailable — review configuration. A selected sorting recipe is not permitted by the profile.",
                profileId);
        }

        var recipes = new List<SortingRecipe>();
        foreach (var recipeId in selectedIds)
        {
            var recipe = await _library.GetRecipeAsync(recipeId, cancellationToken).ConfigureAwait(false);
            if (recipe is null)
            {
                return Unavailable(
                    $"Profile unavailable — review configuration. Sorting recipe \"{recipeId}\" is missing.",
                    profileId);
            }

            if (recipe.SchemaVersion != WorkflowLibraryLimits.CurrentRecipeSchemaVersion ||
                recipe.IsArchived ||
                !recipe.IsEnabled)
            {
                return Unavailable(
                    $"Profile unavailable — review configuration. Sorting recipe \"{recipe.Name}\" is archived, disabled, or incompatible.",
                    profileId);
            }

            recipes.Add(recipe);
        }

        var pluginReferences = profile.PluginContributions
            .Concat(recipes.SelectMany(recipe => recipe.PluginFieldContributions))
            .Distinct()
            .ToArray();
        if (pluginReferences.Length > 0)
        {
            var diagnosticKind = resolutionSource == "manual scan"
                ? PluginDiagnosticKind.WorkflowResolution
                : PluginDiagnosticKind.WatchedFolderResolution;
            if (_pluginResolver is null)
            {
                _pluginDiagnostics?.Record(
                    diagnosticKind,
                    "host",
                    "Required plugin contributions could not be resolved because the registry is unavailable.",
                    "plugin.resolver-unavailable");
                return Unavailable(
                    "Plugin capability unavailable — review workflow profile. Plugin resolution is not configured.",
                    profileId);
            }

            var pluginResolution = _pluginResolver.Resolve(Array.AsReadOnly(pluginReferences));
            if (!pluginResolution.Succeeded)
            {
                _pluginDiagnostics?.Record(
                    diagnosticKind,
                    profile.Id,
                    "A required plugin contribution was unavailable; workflow resolution failed closed.",
                    "plugin.contribution-unavailable");
                return Unavailable(pluginResolution.Message, profileId);
            }

            _pluginDiagnostics?.Record(
                diagnosticKind,
                profile.Id,
                $"Resolved {pluginReferences.Length} exact plugin contribution reference(s).");
        }

        var warnings = new List<string>();
        var files = profile.Files;
        var extraction = profile.Extraction;
        var analysis = profile.Analysis with
        {
            DuplicateAnalysisEnabled = profile.Analysis.DuplicateAnalysisEnabled &&
                                       deterministicAnalysisEnabled,
            ClassificationEnabled = profile.Analysis.ClassificationEnabled &&
                                    deterministicAnalysisEnabled,
            RuleEvaluationEnabled = profile.Analysis.RuleEvaluationEnabled &&
                                    deterministicAnalysisEnabled,
        };
        var ai = profile.Ai;
        var changePlans = profile.ChangePlans;
        if (overrideSettings is not null)
        {
            if (overrideSettings.MaximumFileSizeBytes is long maximum)
            {
                if (maximum <= 0)
                {
                    return Unavailable(
                        "Profile unavailable — review configuration. The file-size override is invalid.",
                        profileId);
                }

                files = files with { MaximumFileSizeBytes = Math.Min(files.MaximumFileSizeBytes, maximum) };
            }

            if (overrideSettings.OcrEnabled is bool ocr)
            {
                extraction = extraction with { OcrEnabled = extraction.OcrEnabled && ocr };
            }

            if (overrideSettings.DuplicateAnalysisEnabled is bool duplicates)
            {
                analysis = analysis with
                {
                    DuplicateAnalysisEnabled = analysis.DuplicateAnalysisEnabled && duplicates,
                };
            }

            if (overrideSettings.AiEnabled is bool aiEnabled)
            {
                ai = ai with { Enabled = ai.Enabled && aiEnabled };
            }

            if (overrideSettings.GenerateChangePlans is bool generatePlans)
            {
                changePlans = changePlans with
                {
                    GenerateChangePlans = changePlans.GenerateChangePlans && generatePlans,
                };
            }
        }

        var applicationSettings = _configurationService.Current;
        if (extraction.OcrEnabled && !applicationSettings.Content.OcrEnabled)
        {
            extraction = extraction with { OcrEnabled = false };
            warnings.Add("The profile requests OCR, but the global OCR setting is off.");
        }
        else if (extraction.OcrEnabled && _ocrService is not null)
        {
            try
            {
                var capability = await _ocrService.GetCapabilityAsync(cancellationToken).ConfigureAwait(false);
                if (!capability.IsAvailable)
                {
                    extraction = extraction with { OcrEnabled = false };
                    warnings.Add($"The profile requests OCR, but the local capability is unavailable: {capability.Message}");
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                extraction = extraction with { OcrEnabled = false };
                warnings.Add($"The profile requests OCR, but capability detection failed safely: {exception.Message}");
            }
        }

        if (ai.Enabled && !applicationSettings.Ai.Enabled)
        {
            ai = ai with { Enabled = false };
            warnings.Add("The profile permits AI, but the global AI switch is off.");
        }
        else if (ai.Enabled && string.IsNullOrWhiteSpace(applicationSettings.Ai.SelectedModel))
        {
            ai = ai with { Enabled = false };
            warnings.Add("The profile permits AI, but no local model is configured.");
        }

        var orderedRecipes = recipes
            .OrderByDescending(recipe => recipe.Priority)
            .ThenBy(recipe => recipe.Id, StringComparer.Ordinal)
            .Select(JsonWorkflowLibraryStore.Clone)
            .ToArray();
        var resolvedPluginSnapshots = pluginReferences
            .Select(reference => _pluginRegistry?.Find(
                reference.PluginId,
                reference.ContributionId,
                reference.ExtensionPoint))
            .Where(registration => registration is not null)
            .Select(registration => new ResolvedPluginContributionSnapshot(
                registration!.PluginId,
                registration.PluginVersion,
                registration.ContributionId,
                registration.ExtensionPoint))
            .ToArray();
        var snapshot = new WorkflowConfigurationSnapshot(
            profile.Id,
            profile.Name,
            profile.Revision,
            profile.ModifiedAtUtc,
            Array.AsReadOnly(orderedRecipes.Select(recipe => new WorkflowRecipeSnapshot(
                recipe.Id,
                recipe.Name,
                recipe.Revision,
                recipe.ModifiedAtUtc,
                recipe.Priority)).ToArray()),
            files with
            {
                IncludedFileTypes = Array.AsReadOnly(files.IncludedFileTypes.ToArray()),
                ExcludedFileTypes = Array.AsReadOnly(files.ExcludedFileTypes.ToArray()),
            },
            extraction,
            analysis,
            ai with { SelectedFileTypes = Array.AsReadOnly(ai.SelectedFileTypes.ToArray()) },
            profile.UncertaintyPolicy,
            changePlans,
            profile.Notifications,
            scanBehavior,
            resolutionSource,
            _timeProvider.GetUtcNow().ToUniversalTime())
        {
            PluginContributions = Array.AsReadOnly(resolvedPluginSnapshots),
        };
        var resolved = new ResolvedWorkflowConfiguration(
            JsonWorkflowLibraryStore.Clone(profile),
            Array.AsReadOnly(orderedRecipes),
            snapshot.Files,
            extraction,
            analysis,
            snapshot.Ai,
            profile.UncertaintyPolicy,
            changePlans,
            profile.Notifications,
            scanBehavior,
            snapshot);
        var result = new WorkflowResolutionResult(
            true,
            resolved,
            $"Resolved profile \"{profile.Name}\" revision {profile.Revision}.",
            Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray()));
        _library.RecordDiagnostic(
            WorkflowDiagnosticKind.Resolution,
            $"Effective configuration resolved from {resolutionSource}; profile revision {profile.Revision}, {orderedRecipes.Length} ordered recipe(s), and {result.Warnings.Count} capability warning(s).",
            profile.Id);
        return result;
    }

    private WorkflowResolutionResult Unavailable(string message, string? itemId)
    {
        _library.RecordDiagnostic(WorkflowDiagnosticKind.Resolution, message, itemId);
        return WorkflowResolutionResult.Unavailable(message);
    }

    private static WorkflowProfileOverride MergeOverrides(
        WorkflowProfileOverride? first,
        WorkflowProfileOverride second) =>
        new(
            NarrowMaximum(first?.MaximumFileSizeBytes, second.MaximumFileSizeBytes),
            NarrowCapability(first?.OcrEnabled, second.OcrEnabled),
            NarrowCapability(first?.DuplicateAnalysisEnabled, second.DuplicateAnalysisEnabled),
            NarrowCapability(first?.AiEnabled, second.AiEnabled),
            NarrowCapability(first?.GenerateChangePlans, second.GenerateChangePlans));

    private static long? NarrowMaximum(long? first, long? second) =>
        first is null
            ? second
            : second is null
                ? first
                : Math.Min(first.Value, second.Value);

    private static bool? NarrowCapability(bool? first, bool? second) =>
        first is null
            ? second
            : second is null
                ? first
                : first.Value && second.Value;
}

public sealed class WorkflowSortingRecipeResolver : IWatchedSortingRecipeResolver
{
    private readonly IWorkflowLibraryService _library;

    public WorkflowSortingRecipeResolver(IWorkflowLibraryService library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public async Task<IReadOnlyList<FileRule>> ResolveAsync(
        string? recipeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipeId) ||
            string.Equals(recipeId, "current", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var recipe = await _library.GetRecipeAsync(recipeId, cancellationToken).ConfigureAwait(false);
        return recipe is null || recipe.IsArchived || !recipe.IsEnabled
            ? []
            : Array.AsReadOnly(recipe.Rules
                .OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .ToArray());
    }
}
