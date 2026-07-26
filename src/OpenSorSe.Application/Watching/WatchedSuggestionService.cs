#pragma warning disable CS1591

using OpenSorSe.Application.AI;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Executor.Models;
using OpenSorSe.Rules.Models;

namespace OpenSorSe.Application.Watching;

public sealed class SessionWatchedSortingRecipeResolver : IWatchedSortingRecipeResolver
{
    private readonly object _gate = new();
    private IReadOnlyList<FileRule> _currentRules = Array.Empty<FileRule>();

    public void SetCurrentRules(IReadOnlyList<FileRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Any(rule => rule is null))
        {
            throw new ArgumentException("A sorting recipe cannot contain null rules.", nameof(rules));
        }

        lock (_gate)
        {
            _currentRules = Array.AsReadOnly(rules.ToArray());
        }
    }

    public Task<IReadOnlyList<FileRule>> ResolveAsync(string? recipeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(recipeId, "current", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<FileRule>>(Array.Empty<FileRule>());
        }

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<FileRule>>(
                Array.AsReadOnly(_currentRules.ToArray()));
        }
    }
}

public sealed class WatchedSuggestionService : IWatchedSuggestionService
{
    private const int MaximumAiBatchSize = 12;
    private const int MaximumAiItemsPerRun = 120;
    private readonly ISuggestionChangePlanFactory _planFactory;
    private readonly IAiSuggestionService _aiService;
    private readonly IConfigurationService _configurationService;

    public WatchedSuggestionService(
        ISuggestionChangePlanFactory planFactory,
        IAiSuggestionService aiService,
        IConfigurationService configurationService)
    {
        _planFactory = planFactory ?? throw new ArgumentNullException(nameof(planFactory));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    public async Task<WatchedSuggestionResult> CreateSuggestionsAsync(
        WatchedFolderConfiguration configuration,
        ResultsSnapshot snapshot,
        IReadOnlyList<ResultFile> affectedFiles,
        bool suppressSuggestions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(affectedFiles);
        if (suppressSuggestions)
        {
            return new WatchedSuggestionResult(
                Array.Empty<ChangePlan>(),
                false,
                false,
                ["Suggestions were suppressed because this batch reconciled an approved OpenSorSe operation."]);
        }

        var plans = new List<ChangePlan>();
        var warnings = new List<string>();
        if (configuration.DeterministicAnalysisEnabled && snapshot.PlannedOperations.Count > 0)
        {
            try
            {
                plans.Add(await _planFactory.CreateRulePlanAsync(snapshot, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
            {
                warnings.Add($"Deterministic suggestions could not be captured in a Change Plan: {exception.Message}");
            }
        }

        var settings = _configurationService.Current.Ai;
        if (!configuration.AiAnalysisEnabled || affectedFiles.Count == 0)
        {
            return new WatchedSuggestionResult(
                Array.AsReadOnly(plans.ToArray()),
                false,
                false,
                Array.AsReadOnly(warnings.ToArray()));
        }

        if (!settings.Enabled)
        {
            warnings.Add("AI analysis is enabled for this watched folder, but the global AI switch is off.");
            return new WatchedSuggestionResult(
                Array.AsReadOnly(plans.ToArray()),
                false,
                false,
                Array.AsReadOnly(warnings.ToArray()));
        }

        var aiAttempted = false;
        var aiFailed = false;
        var completedFileIds = new HashSet<string>(StringComparer.Ordinal);
        var failedFileIds = new HashSet<string>(StringComparer.Ordinal);
        var orderedAffectedFiles = affectedFiles
            .OrderBy(file => file.Id, StringComparer.Ordinal)
            .Take(MaximumAiItemsPerRun)
            .ToArray();
        if (affectedFiles.Count > orderedAffectedFiles.Length)
        {
            warnings.Add(
                $"{affectedFiles.Count - orderedAffectedFiles.Length} AI item(s) remain pending after the bounded per-run backlog limit.");
        }

        if (settings.FolderStructureSuggestionsEnabled)
        {
            var existingFolders = snapshot.Directories
                .Select(directory => directory.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(128)
                .ToArray();
            foreach (var batch in orderedAffectedFiles.Chunk(MaximumAiBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                aiAttempted = true;
                try
                {
                    var result = await _aiService.GenerateFolderStructureAsync(
                        new AiFolderStructureRequest(Array.AsReadOnly(batch), Array.AsReadOnly(existingFolders)),
                        settings,
                        cancellationToken).ConfigureAwait(false);
                    if (result.Plan is not null && result.State == AiAvailabilityState.ModelSelected)
                    {
                        plans.Add(await _planFactory.CreateFolderStructurePlanAsync(
                            Array.AsReadOnly(batch),
                            result.Plan,
                            snapshot.SessionId,
                            cancellationToken).ConfigureAwait(false));
                        completedFileIds.UnionWith(batch.Select(file => file.Id));
                    }
                    else if (result.State == AiAvailabilityState.NoSuggestion)
                    {
                        completedFileIds.UnionWith(batch.Select(file => file.Id));
                    }
                    else
                    {
                        aiFailed = true;
                        failedFileIds.UnionWith(batch.Select(file => file.Id));
                        warnings.Add(result.Message);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    aiFailed = true;
                    failedFileIds.UnionWith(batch.Select(file => file.Id));
                    warnings.Add($"Optional AI analysis failed safely: {exception.Message}");
                }
            }
        }
        else if (settings.FileRenameSuggestionsEnabled)
        {
            foreach (var file in orderedAffectedFiles.Take(MaximumAiBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                aiAttempted = true;
                try
                {
                    var siblings = snapshot.Files
                        .Where(candidate =>
                            !string.Equals(candidate.Id, file.Id, StringComparison.Ordinal) &&
                            WatchedFolderPathPolicy.PathComparer.Equals(
                                Path.GetDirectoryName(candidate.FullPath),
                                Path.GetDirectoryName(file.FullPath)))
                        .Select(candidate => candidate.DisplayFileName)
                        .Take(128)
                        .ToArray();
                    var result = await _aiService.GenerateFileRenameAsync(
                        new AiFileRenameRequest(file, Array.AsReadOnly(siblings)),
                        settings,
                        cancellationToken).ConfigureAwait(false);
                    if (result.Suggestion is not null && result.State == AiAvailabilityState.ModelSelected)
                    {
                        plans.Add(await _planFactory.CreateRenamePlanAsync(
                            file,
                            result.Suggestion,
                            result.Suggestion.SuggestedFileName,
                            snapshot.SessionId,
                            cancellationToken).ConfigureAwait(false));
                        completedFileIds.Add(file.Id);
                    }
                    else if (result.State == AiAvailabilityState.NoSuggestion)
                    {
                        completedFileIds.Add(file.Id);
                    }
                    else
                    {
                        aiFailed = true;
                        failedFileIds.Add(file.Id);
                        warnings.Add(result.Message);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    aiFailed = true;
                    failedFileIds.Add(file.Id);
                    warnings.Add($"Optional AI analysis failed safely: {exception.Message}");
                }
            }
        }
        else
        {
            warnings.Add("AI analysis is enabled for this watched folder, but no compatible suggestion capability is enabled.");
        }

        return new WatchedSuggestionResult(
            Array.AsReadOnly(plans.ToArray()),
            aiAttempted,
            aiFailed,
            Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray()))
        {
            CompletedAiFileIds = completedFileIds,
            FailedAiFileIds = failedFileIds,
        };
    }
}
