#pragma warning disable CS1591

using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Logging;
using OpenSorSe.Rules;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Watching;

/// <summary>
/// Reconciles one watched root against its dedicated catalogue and analyses only verified stable changes.
/// </summary>
/// <remarks>
/// Processing is staged: discover actual state, apply path/ignore policy,
/// observe stability, enrich changed files, update the catalogue, and create
/// optional suggestions. Deterministic catalogue changes survive optional AI
/// failure. This service owns no watcher lifetime and cannot approve or execute
/// a Change Plan.
/// </remarks>
public sealed class WatchedFolderProcessor : IWatchedFolderProcessor
{
    private readonly IWatchedFolderCatalogueStore _catalogueStore;
    private readonly IWatchedFileSystem _fileSystem;
    private readonly IFileStabilityChecker _stabilityChecker;
    private readonly IFileMetadataReader _metadataReader;
    private readonly IContentIndexingService? _contentIndexingService;
    private readonly IWorkflowConfigurationResolver? _workflowResolver;
    private readonly IFileHasher _hasher;
    private readonly IFileClassifier _classifier;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly IRuleEngine _ruleEngine;
    private readonly IActionPlanner _actionPlanner;
    private readonly IConflictResolver _conflictResolver;
    private readonly IWatchedSortingRecipeResolver _recipeResolver;
    private readonly IWatchedSuggestionService _suggestionService;
    private readonly WatchedFolderPathPolicy _pathPolicy;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public WatchedFolderProcessor(
        IWatchedFolderCatalogueStore catalogueStore,
        IWatchedFileSystem fileSystem,
        IFileStabilityChecker stabilityChecker,
        IFileMetadataReader metadataReader,
        IFileHasher hasher,
        IFileClassifier classifier,
        IDuplicateDetector duplicateDetector,
        IRuleEngine ruleEngine,
        IActionPlanner actionPlanner,
        IConflictResolver conflictResolver,
        IWatchedSortingRecipeResolver recipeResolver,
        IWatchedSuggestionService suggestionService,
        WatchedFolderPathPolicy pathPolicy,
        ILoggingService loggingService,
        IContentIndexingService? contentIndexingService = null,
        TimeProvider? timeProvider = null,
        IWorkflowConfigurationResolver? workflowResolver = null)
    {
        _catalogueStore = catalogueStore ?? throw new ArgumentNullException(nameof(catalogueStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _stabilityChecker = stabilityChecker ?? throw new ArgumentNullException(nameof(stabilityChecker));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _actionPlanner = actionPlanner ?? throw new ArgumentNullException(nameof(actionPlanner));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _recipeResolver = recipeResolver ?? throw new ArgumentNullException(nameof(recipeResolver));
        _suggestionService = suggestionService ?? throw new ArgumentNullException(nameof(suggestionService));
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(WatchedFolderProcessor));
        _contentIndexingService = contentIndexingService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _workflowResolver = workflowResolver;
    }

    public async Task<WatchedFolderProcessResult> ProcessAsync(
        WatchedFolderConfiguration configuration,
        WatchedChangeBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(batch);
        if (!string.Equals(configuration.Id, batch.ConfigurationId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The change batch belongs to a different watched folder.", nameof(batch));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.DirectoryExists(configuration.FolderPath))
        {
            throw new DirectoryNotFoundException("The watched folder is unavailable.");
        }

        var pipelineWarnings = new List<string>();
        if (_workflowResolver is not null)
        {
            var workflowResolution = await _workflowResolver.ResolveForWatchedFolderAsync(
                configuration,
                cancellationToken).ConfigureAwait(false);
            if (!workflowResolution.IsAvailable || workflowResolution.Configuration is null)
            {
                throw new WorkflowProfileUnavailableException(workflowResolution.Message);
            }

            var effective = workflowResolution.Configuration;
            pipelineWarnings.AddRange(workflowResolution.Warnings);
            configuration = configuration with
            {
                MaximumFileSizeBytes = effective.Files.MaximumFileSizeBytes,
                DeterministicAnalysisEnabled =
                    effective.Analysis.ClassificationEnabled ||
                    effective.Analysis.DuplicateAnalysisEnabled ||
                    effective.Analysis.RuleEvaluationEnabled,
                AiAnalysisEnabled = effective.Ai.Enabled,
                SortingRecipeIds = effective.Recipes.Select(recipe => recipe.Id).ToArray(),
                SortingRecipeId = effective.Recipes.FirstOrDefault()?.Id,
                EffectiveWorkflow = effective,
                RuntimeScanReason = batch.Reason,
            };
        }

        var now = _timeProvider.GetUtcNow().ToUniversalTime();
        var existing = await _catalogueStore.GetAsync(configuration.CatalogueId, cancellationToken).ConfigureAwait(false);
        ValidateExistingScope(configuration, existing);
        var fullReconciliation = existing is null ||
                                 batch.RequiresFullReconciliation ||
                                 IsReconciliationReason(batch.Reason) ||
                                 batch.Hints.Any(hint => hint.IsDirectory);
        _logger.LogInformation(
            "Watched scan decision for {ConfigurationId}: {Mode}, reason {Reason}, {HintCount} normalized hint(s).",
            configuration.Id,
            fullReconciliation ? "full reconciliation" : "targeted incremental",
            batch.Reason,
            batch.Hints.Count);

        var existingFiles = existing?.Files ?? Array.Empty<WatchedFileState>();
        var existingDirectories = existing?.Directories ?? Array.Empty<WatchedDirectoryState>();
        var discovery = fullReconciliation
            ? await DiscoverFullAsync(configuration, cancellationToken).ConfigureAwait(false)
            : await DiscoverTargetedAsync(configuration, existingFiles, existingDirectories, batch, cancellationToken)
                .ConfigureAwait(false);
        discovery = ApplyProfileFileSelection(discovery, configuration.EffectiveWorkflow?.Files);

        var comparison = Compare(existingFiles, discovery.Files, fullReconciliation);
        var scanBehavior = configuration.EffectiveWorkflow?.ScanBehavior;
        var stableWork = new List<FileWorkItem>();
        var finalStates = new Dictionary<string, WatchedFileState>(StringComparer.Ordinal);
        if (scanBehavior?.ReconcileMissingItems == false)
        {
            var currentIds = comparison.Current
                .Select(item => item.Probe.StableId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var existingFile in existingFiles.Where(file => !currentIds.Contains(file.StableId)))
            {
                finalStates[existingFile.StableId] = existingFile;
            }
        }

        var deferred = 0;
        var unresolved = 0;
        foreach (var item in comparison.Current)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.RequiresContentProcessing &&
                scanBehavior?.ReanalyseChangedContentOnly is not false)
            {
                var preserved = CreatePreservedState(item, now, batch.Reason);
                finalStates[item.Probe.StableId] = scanBehavior?.PreserveUnchangedAnalysis == false
                    ? preserved with
                    {
                        ContentHash = null,
                        Category = null,
                        DuplicateStatus = DuplicateStatus.Unknown,
                        DuplicateGroupId = null,
                        AiAnalysisState = WatchedAiAnalysisState.NotRequested,
                        AiLastAttemptUtc = null,
                    }
                    : preserved;
                continue;
            }

            var stability = await _stabilityChecker.WaitForStableAsync(
                item.Probe.FullPath,
                WatchedFolderLimits.DefaultStabilityObservation,
                maximumAttempts: 3,
                cancellationToken).ConfigureAwait(false);
            if (!stability.IsStable || stability.Probe is null)
            {
                deferred++;
                unresolved++;
                finalStates[item.Probe.StableId] = new WatchedFileState(
                    item.Probe.StableId,
                    item.Probe.FullPath,
                    item.Probe.SizeInBytes,
                    item.Probe.CreationTimeUtc,
                    item.Probe.LastWriteTimeUtc,
                    item.Probe.Attributes,
                    null,
                    null,
                    DuplicateStatus.Unknown,
                    null,
                    now,
                    WatchedItemReprocessReason.DeferredUntilStable);
                _logger.LogInformation(
                    "Watched file processing was deferred after {AttemptCount} stability checks.",
                    stability.Attempts);
                continue;
            }

            var stableProbe = stability.Probe with { StableId = item.Probe.StableId };
            stableWork.Add(item with { Probe = stableProbe });
        }

        var processed = await ProcessChangedFilesAsync(
            stableWork,
            configuration,
            batch,
            now,
            pipelineWarnings,
            cancellationToken).ConfigureAwait(false);
        foreach (var state in processed)
        {
            finalStates[state.StableId] = state;
        }

        var combinedEntries = finalStates.Values
            .OrderBy(state => state.FullPath, WatchedFolderPathPolicy.PathComparer)
            .Select(ToFileEntry)
            .ToArray();
        DuplicateDetectionResult? duplicates = null;
        if ((configuration.EffectiveWorkflow?.Analysis.DuplicateAnalysisEnabled ??
             configuration.DeterministicAnalysisEnabled) &&
            combinedEntries.Length > 0)
        {
            duplicates = await _duplicateDetector.DetectAsync(combinedEntries, cancellationToken).ConfigureAwait(false);
            finalStates = duplicates.Files.ToDictionary(
                entry => FindStableId(finalStates.Values, entry.FullPath),
                entry =>
                {
                    var stableId = FindStableId(finalStates.Values, entry.FullPath);
                    var state = finalStates[stableId];
                    return state with
                    {
                        DuplicateStatus = entry.Duplicate?.Status ?? DuplicateStatus.Unknown,
                        DuplicateGroupId = entry.Duplicate?.GroupId,
                    };
                },
                StringComparer.Ordinal);
            if (duplicates.Issues.Count > 0)
            {
                pipelineWarnings.Add($"{duplicates.Issues.Count} duplicate-analysis item(s) remain unavailable.");
            }
        }

        var changedStableIds = comparison.Current
            .Where(item =>
                scanBehavior?.ReanalyseChangedContentOnly == false ||
                item.IsNew ||
                item.IsContentChanged ||
                item.IsMetadataChanged ||
                item.IsRenamedOrMoved ||
                item.Previous?.LastReprocessReason == WatchedItemReprocessReason.DeferredUntilStable)
            .Select(item => item.Probe.StableId)
            .ToHashSet(StringComparer.Ordinal);
        var affectedStableIds = batch.Reason == WatchedScanReason.AiRetry
            ? finalStates.Values
                .Where(state => state.AiAnalysisState is
                    WatchedAiAnalysisState.Pending or WatchedAiAnalysisState.Failed)
                .Select(state => state.StableId)
                .ToHashSet(StringComparer.Ordinal)
            : changedStableIds;
        if (batch.Reason != WatchedScanReason.AiRetry)
        {
            foreach (var stableId in affectedStableIds)
            {
                if (!finalStates.TryGetValue(stableId, out var state) ||
                    state.LastReprocessReason == WatchedItemReprocessReason.DeferredUntilStable)
                {
                    continue;
                }

                finalStates[stableId] = state with
                {
                    AiAnalysisState = configuration.AiAnalysisEnabled &&
                                      !batch.SuppressSuggestions &&
                                      batch.Reason != WatchedScanReason.OpenSorSeExecution
                        ? WatchedAiAnalysisState.Pending
                        : WatchedAiAnalysisState.NotRequested,
                    AiLastAttemptUtc = null,
                };
            }
        }

        var ruleAffectedStableIds = batch.Reason == WatchedScanReason.AiRetry
            ? new HashSet<string>(StringComparer.Ordinal)
            : affectedStableIds;
        var planning = await PlanAffectedRulesAsync(
            configuration,
            finalStates,
            ruleAffectedStableIds,
            cancellationToken).ConfigureAwait(false);
        pipelineWarnings.AddRange(planning.Warnings);

        var snapshot = CreateResultsSnapshot(
            configuration,
            batch,
            finalStates.Values,
            discovery.Directories,
            planning.Operations,
            pipelineWarnings,
            now);
        var isReconciliation = fullReconciliation || IsReconciliationReason(batch.Reason);
        var catalogue = new WatchedFolderCatalogue(
            WatchedFolderLimits.CurrentCatalogueSchemaVersion,
            configuration.CatalogueId,
            configuration.Id,
            configuration.FolderPath,
            now,
            Array.AsReadOnly(finalStates.Values
                .OrderBy(state => state.FullPath, WatchedFolderPathPolicy.PathComparer)
                .ToArray()),
            Array.AsReadOnly(discovery.Directories
                .OrderBy(directory => directory.FullPath, WatchedFolderPathPolicy.PathComparer)
                .ToArray()),
            isReconciliation ? now : existing?.LastReconciliationUtc,
            unresolved > 0)
        {
            Workflow = configuration.EffectiveWorkflow?.Snapshot ?? existing?.Workflow,
        };
        await _catalogueStore.UpsertAsync(catalogue, cancellationToken).ConfigureAwait(false);

        var affectedResultFiles = snapshot.Files
            .Where(file => affectedStableIds.Contains(file.Id))
            .ToArray();
        WatchedSuggestionResult suggestions;
        try
        {
            suggestions = await _suggestionService.CreateSuggestionsAsync(
                configuration,
                snapshot,
                Array.AsReadOnly(affectedResultFiles),
                batch.SuppressSuggestions || batch.Reason == WatchedScanReason.OpenSorSeExecution,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Suggestion generation failed after the watched catalogue update; deterministic scan results were retained.");
            suggestions = new WatchedSuggestionResult(
                Array.Empty<OpenSorSe.Executor.Models.ChangePlan>(),
                configuration.AiAnalysisEnabled,
                configuration.AiAnalysisEnabled,
                ["Suggestion generation failed safely; the catalogue update succeeded."]);
        }

        pipelineWarnings.AddRange(suggestions.Warnings);
        if (configuration.AiAnalysisEnabled && affectedStableIds.Count > 0)
        {
            var failedIds = suggestions.FailedAiFileIds.Count > 0
                ? suggestions.FailedAiFileIds
                : suggestions.AiFailed
                    ? affectedStableIds
                    : new HashSet<string>(StringComparer.Ordinal);
            foreach (var stableId in affectedStableIds)
            {
                if (!finalStates.TryGetValue(stableId, out var state))
                {
                    continue;
                }

                if (failedIds.Contains(stableId))
                {
                    finalStates[stableId] = state with
                    {
                        AiAnalysisState = WatchedAiAnalysisState.Failed,
                        AiLastAttemptUtc = now,
                    };
                }
                else if (suggestions.CompletedAiFileIds.Contains(stableId))
                {
                    finalStates[stableId] = state with
                    {
                        AiAnalysisState = WatchedAiAnalysisState.Completed,
                        AiLastAttemptUtc = now,
                    };
                }
            }

            catalogue = catalogue with
            {
                Files = Array.AsReadOnly(finalStates.Values
                    .OrderBy(state => state.FullPath, WatchedFolderPathPolicy.PathComparer)
                    .ToArray()),
            };
            await _catalogueStore.UpsertAsync(catalogue, cancellationToken).ConfigureAwait(false);
        }

        var summary = new WatchedChangeSummary(
            comparison.Added,
            comparison.Updated,
            comparison.RenamedOrMoved,
            scanBehavior?.ReconcileMissingItems == false ? 0 : comparison.Removed,
            deferred,
            discovery.Ignored,
            unresolved);
        return new WatchedFolderProcessResult(
            configuration.Id,
            batch.BatchId,
            batch.Reason,
            summary,
            catalogue,
            suggestions.Plans,
            suggestions.AiAttempted,
            suggestions.AiFailed,
            Array.AsReadOnly(pipelineWarnings.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private async Task<DiscoveryResult> DiscoverFullAsync(
        WatchedFolderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var probes = await _fileSystem.EnumerateAsync(configuration, cancellationToken).ConfigureAwait(false);
        return new DiscoveryResult(
            probes.Where(probe => !probe.IsDirectory).ToArray(),
            probes.Where(probe => probe.IsDirectory)
                .Select(probe => new WatchedDirectoryState(
                    probe.FullPath,
                    probe.LastWriteTimeUtc,
                    probe.Attributes))
                .ToArray(),
            0);
    }

    private async Task<DiscoveryResult> DiscoverTargetedAsync(
        WatchedFolderConfiguration configuration,
        IReadOnlyList<WatchedFileState> existingFiles,
        IReadOnlyList<WatchedDirectoryState> existingDirectories,
        WatchedChangeBatch batch,
        CancellationToken cancellationToken)
    {
        var desired = existingFiles.ToDictionary(
            file => file.StableId,
            ToProbe,
            StringComparer.Ordinal);
        var byOriginalPath = existingFiles.ToDictionary(
            file => file.FullPath,
            file => file,
            WatchedFolderPathPolicy.PathComparer);
        var ignored = 0;
        foreach (var hint in NormalizeHints(batch.Hints))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(hint.OldPath))
            {
                RemoveByPath(desired, hint.OldPath);
            }

            if (string.IsNullOrWhiteSpace(hint.Path) ||
                !_pathPolicy.IsWithinRoot(configuration.FolderPath, hint.Path))
            {
                continue;
            }

            var probe = await _fileSystem.ProbeAsync(hint.Path, cancellationToken).ConfigureAwait(false);
            if (probe is null)
            {
                RemoveByPath(desired, hint.Path);
                continue;
            }

            if (probe.IsDirectory)
            {
                continue;
            }

            if (_pathPolicy.ShouldIgnore(configuration, probe.FullPath, probe.Attributes, probe.SizeInBytes))
            {
                ignored++;
                RemoveByPath(desired, probe.FullPath);
                continue;
            }

            var existingAtPath = byOriginalPath.GetValueOrDefault(probe.FullPath);
            if (existingAtPath is not null)
            {
                desired.Remove(existingAtPath.StableId);
                probe = probe with { StableId = existingAtPath.StableId };
            }

            var oldFromRename = !string.IsNullOrWhiteSpace(hint.OldPath)
                ? byOriginalPath.GetValueOrDefault(hint.OldPath)
                : null;
            if (oldFromRename is not null)
            {
                desired.Remove(oldFromRename.StableId);
                probe = probe with { StableId = oldFromRename.StableId };
            }

            desired[probe.StableId] = probe;
        }

        return new DiscoveryResult(
            desired.Values.ToArray(),
            existingDirectories.ToArray(),
            ignored);
    }

    private static ComparisonResult Compare(
        IReadOnlyList<WatchedFileState> existingFiles,
        IReadOnlyList<WatchedFileProbe> currentProbes,
        bool fullReconciliation)
    {
        var existingById = existingFiles.ToDictionary(file => file.StableId, StringComparer.Ordinal);
        var existingByPath = existingFiles.ToDictionary(
            file => file.FullPath,
            WatchedFolderPathPolicy.PathComparer);
        var current = new List<FileWorkItem>(currentProbes.Count);
        var matchedExistingIds = new HashSet<string>(StringComparer.Ordinal);
        var added = 0;
        var updated = 0;
        var renamed = 0;
        foreach (var originalProbe in currentProbes)
        {
            var probe = originalProbe;
            existingById.TryGetValue(probe.StableId, out var previous);
            if (previous is null && existingByPath.TryGetValue(probe.FullPath, out var pathMatch))
            {
                previous = pathMatch;
                probe = probe with { StableId = previous.StableId };
            }

            var isNew = previous is null;
            var isRenamed = previous is not null &&
                            !WatchedFolderPathPolicy.PathComparer.Equals(previous.FullPath, probe.FullPath);
            var isContentChanged = previous is not null &&
                                   (previous.SizeInBytes != probe.SizeInBytes ||
                                    previous.LastWriteTimeUtc != probe.LastWriteTimeUtc);
            var isMetadataChanged = previous is not null &&
                                    !isContentChanged &&
                                    (previous.Attributes != probe.Attributes ||
                                     previous.CreationTimeUtc != probe.CreationTimeUtc);
            if (previous is not null)
            {
                matchedExistingIds.Add(previous.StableId);
            }

            if (isNew)
            {
                added++;
            }
            else if (isRenamed)
            {
                renamed++;
            }

            if (isContentChanged || isMetadataChanged)
            {
                updated++;
            }

            current.Add(new FileWorkItem(
                probe,
                previous,
                isNew,
                isRenamed,
                isContentChanged,
                isMetadataChanged));
        }

        var removed = fullReconciliation
            ? existingFiles.Count(file => !matchedExistingIds.Contains(file.StableId))
            : existingFiles.Count(file => current.All(item =>
                !string.Equals(item.Previous?.StableId, file.StableId, StringComparison.Ordinal)));
        return new ComparisonResult(current, added, updated, renamed, removed);
    }

    private async Task<IReadOnlyList<WatchedFileState>> ProcessChangedFilesAsync(
        IReadOnlyList<FileWorkItem> work,
        WatchedFolderConfiguration configuration,
        WatchedChangeBatch batch,
        DateTimeOffset now,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (work.Count == 0)
        {
            return Array.Empty<WatchedFileState>();
        }

        var entries = work.Select(item => new FileEntry(item.Probe.FullPath)).ToArray();
        IReadOnlyList<FileEntry> metadataFiles = entries;
        if (configuration.EffectiveWorkflow?.Extraction.MetadataEnabled is not false)
        {
            var metadata = await _metadataReader.ReadAsync(entries, cancellationToken).ConfigureAwait(false);
            metadataFiles = metadata.Files;
            if (metadata.Issues.Count > 0)
            {
                warnings.Add($"{metadata.Issues.Count} file metadata item(s) were only partially available.");
            }
        }

        IReadOnlyList<FileEntry> enriched = metadataFiles;
        if (configuration.EffectiveWorkflow?.Extraction.MetadataEnabled ??
            configuration.DeterministicAnalysisEnabled)
        {
            if (_contentIndexingService is not null &&
                (configuration.EffectiveWorkflow is null ||
                 configuration.EffectiveWorkflow.Extraction.TextEnabled ||
                 configuration.EffectiveWorkflow.Extraction.OcrEnabled))
            {
                try
                {
                    var content = await _contentIndexingService.IndexAsync(
                        metadataFiles,
                        configuration.EffectiveWorkflow is null
                            ? null
                            : new ContentIndexingOptions(
                                configuration.EffectiveWorkflow.Extraction.MetadataEnabled,
                                configuration.EffectiveWorkflow.Extraction.TextEnabled,
                                configuration.EffectiveWorkflow.Extraction.OcrEnabled,
                                configuration.EffectiveWorkflow.Extraction.OcrOnlyWhenTextUnavailable,
                                configuration.EffectiveWorkflow.Extraction.OcrLanguage,
                                configuration.EffectiveWorkflow.Extraction.MaximumPagesPerDocument,
                                configuration.EffectiveWorkflow.Files.MaximumFileSizeBytes),
                        cancellationToken).ConfigureAwait(false);
                    if (content.FailedCount > 0)
                    {
                        warnings.Add($"{content.FailedCount} content extraction item(s) failed safely.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Watched content extraction failed safely; metadata processing continued.");
                    warnings.Add("Content extraction was unavailable for this batch.");
                }
            }

            IReadOnlyList<FileEntry> analysisInput = metadataFiles;
            if (configuration.EffectiveWorkflow?.Analysis.DuplicateAnalysisEnabled ??
                configuration.DeterministicAnalysisEnabled)
            {
                var hashing = await _hasher.HashAsync(metadataFiles, cancellationToken).ConfigureAwait(false);
                if (hashing.Issues.Count > 0)
                {
                    warnings.Add($"{hashing.Issues.Count} file hash item(s) remain unavailable.");
                }

                analysisInput = hashing.Files;
            }

            if (configuration.EffectiveWorkflow?.Analysis.ClassificationEnabled ??
                configuration.DeterministicAnalysisEnabled)
            {
                var classification = await _classifier.ClassifyAsync(
                    analysisInput,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (classification.Issues.Count > 0)
                {
                    warnings.Add($"{classification.Issues.Count} classification item(s) remain unknown.");
                }

                analysisInput = classification.Files;
            }

            enriched = analysisInput;
        }

        var byPath = work.ToDictionary(
            item => item.Probe.FullPath,
            WatchedFolderPathPolicy.PathComparer);
        return Array.AsReadOnly(enriched.Select(entry =>
        {
            var item = byPath[entry.FullPath];
            return new WatchedFileState(
                item.Probe.StableId,
                item.Probe.FullPath,
                entry.Metadata?.SizeInBytes ?? item.Probe.SizeInBytes,
                entry.Metadata?.CreationTimeUtc ?? item.Probe.CreationTimeUtc,
                entry.Metadata?.LastWriteTimeUtc ?? item.Probe.LastWriteTimeUtc,
                entry.Metadata?.Attributes ?? item.Probe.Attributes,
                entry.Hash?.Value,
                entry.Classification?.Category,
                DuplicateStatus.Unknown,
                null,
                now,
                DetermineReason(item, batch.Reason));
        }).ToArray());
    }

    private async Task<PlanningResult> PlanAffectedRulesAsync(
        WatchedFolderConfiguration configuration,
        IReadOnlyDictionary<string, WatchedFileState> states,
        IReadOnlySet<string> affectedStableIds,
        CancellationToken cancellationToken)
    {
        if (!(configuration.EffectiveWorkflow?.Analysis.RuleEvaluationEnabled ??
              configuration.DeterministicAnalysisEnabled) ||
            affectedStableIds.Count == 0)
        {
            return new PlanningResult(Array.Empty<PlannedOperation>(), Array.Empty<string>());
        }

        var recipeIds = configuration.EffectiveWorkflow is not null
            ? configuration.EffectiveWorkflow.Recipes.Select(recipe => recipe.Id).ToArray()
            : configuration.SortingRecipeIds.Count > 0
                ? configuration.SortingRecipeIds.ToArray()
                : string.IsNullOrWhiteSpace(configuration.SortingRecipeId)
                    ? []
                    : [configuration.SortingRecipeId];
        var rules = await _recipeResolver.ResolveManyAsync(
            recipeIds,
            cancellationToken).ConfigureAwait(false);
        if (rules.Count == 0)
        {
            return new PlanningResult(Array.Empty<PlannedOperation>(), Array.Empty<string>());
        }

        var affected = states
            .Where(pair => affectedStableIds.Contains(pair.Key))
            .Select(pair => ToFileEntry(pair.Value))
            .ToArray();
        var evaluation = await _ruleEngine.EvaluateAsync(affected, rules, cancellationToken).ConfigureAwait(false);
        var planned = await _actionPlanner.PlanAsync(evaluation.Decisions, cancellationToken).ConfigureAwait(false);
        var resolved = await _conflictResolver.ResolveAsync(
            planned.Operations,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var warnings = planned.Issues.Select(issue => issue.Message)
            .Concat(resolved.Issues.Select(issue => issue.Message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new PlanningResult(resolved.Operations, warnings);
    }

    private static ResultsSnapshot CreateResultsSnapshot(
        WatchedFolderConfiguration configuration,
        WatchedChangeBatch batch,
        IEnumerable<WatchedFileState> states,
        IReadOnlyList<WatchedDirectoryState> directories,
        IReadOnlyList<PlannedOperation> operations,
        IReadOnlyList<string> warnings,
        DateTimeOffset now)
    {
        var stateArray = states.OrderBy(state => state.FullPath, WatchedFolderPathPolicy.PathComparer).ToArray();
        var operationByPath = operations
            .GroupBy(operation => operation.SourcePath, WatchedFolderPathPolicy.PathComparer)
            .ToDictionary(group => group.Key, group => group.First(), WatchedFolderPathPolicy.PathComparer);
        var files = stateArray.Select(state => new ResultFile(
            state.StableId,
            state.FullPath,
            Path.GetFileName(state.FullPath),
            Path.GetExtension(state.FullPath).ToLowerInvariant(),
            state.SizeInBytes,
            state.LastWriteTimeUtc,
            state.Category,
            state.Category?.ToString() ?? "Unclassified",
            state.DuplicateStatus,
            state.DuplicateGroupId,
            operationByPath.ContainsKey(state.FullPath))
        {
            CreationTimeUtc = state.CreationTimeUtc,
        }).ToArray();
        var fileByPath = files.ToDictionary(file => file.FullPath, WatchedFolderPathPolicy.PathComparer);
        var resultOperations = operations.Select(operation => new ResultPlannedOperation(
            operation.OperationId,
            operation.Kind,
            fileByPath.GetValueOrDefault(operation.SourcePath)?.Id,
            operation.DestinationPath,
            operation.SelectedRuleName)).ToArray();
        var duplicateGroups = files
            .Where(file => file.DuplicateStatus == DuplicateStatus.Duplicate &&
                           !string.IsNullOrWhiteSpace(file.DuplicateGroupId))
            .GroupBy(file => file.DuplicateGroupId!, StringComparer.Ordinal)
            .Where(group => group.Count() >= 2)
            .Select((group, index) =>
            {
                var members = group.Select(file => file.Id).ToArray();
                var sizes = group.Select(file => file.SizeInBytes).Distinct().ToArray();
                var commonSize = sizes.Length == 1 ? sizes[0] : null;
                long? reclaimable = null;
                if (commonSize is { } size)
                {
                    try
                    {
                        reclaimable = checked(size * (members.Length - 1));
                    }
                    catch (OverflowException)
                    {
                        reclaimable = null;
                    }
                }

                return new ResultDuplicateGroup(
                    group.Key,
                    index + 1,
                    Array.AsReadOnly(members),
                    members.Length,
                    commonSize,
                    reclaimable);
            })
            .ToArray();
        var issues = warnings.Select(warning =>
            new ResultIssue("Watched folders", ResultIssueSeverity.Warning, Truncate(warning, 512), null)).ToArray();
        var resultDirectories = directories.Select(directory =>
            new ResultDirectory(directory.FullPath, Path.GetFileName(directory.FullPath))).ToArray();
        var statistics = new ResultsSnapshotStatistics(
            files.LongLength,
            resultDirectories.LongLength,
            duplicateGroups.LongLength,
            duplicateGroups.Sum(group => (long)group.MemberCount),
            resultOperations.LongLength,
            issues.LongCount(issue => issue.Severity == ResultIssueSeverity.Warning),
            issues.LongCount(issue => issue.Severity == ResultIssueSeverity.Error));
        return new ResultsSnapshot(
            $"watched:{configuration.Id}:{batch.BatchId}",
            batch.FirstDetectedAtUtc.ToUniversalTime(),
            now,
            Array.AsReadOnly(files),
            Array.AsReadOnly(resultDirectories),
            Array.AsReadOnly(duplicateGroups),
            Array.AsReadOnly(resultOperations),
            Array.AsReadOnly(issues),
            statistics,
            configuration.EffectiveWorkflow?.Analysis.DuplicateAnalysisEnabled ??
            configuration.DeterministicAnalysisEnabled)
        {
            Workflow = configuration.EffectiveWorkflow?.Snapshot,
        };
    }

    private static IReadOnlyList<WatchedFolderHint> NormalizeHints(IReadOnlyList<WatchedFolderHint> hints) =>
        Array.AsReadOnly(hints
            .Where(hint => hint is not null)
            .GroupBy(
                hint => $"{hint.Kind}|{hint.OldPath}|{hint.Path}",
                WatchedFolderPathPolicy.PathComparer)
            .Select(group => group.OrderByDescending(hint => hint.DetectedAtUtc).First())
            .OrderBy(hint => hint.DetectedAtUtc)
            .ToArray());

    private static DiscoveryResult ApplyProfileFileSelection(
        DiscoveryResult discovery,
        WorkflowFileSelectionOptions? files)
    {
        if (files is null)
        {
            return discovery;
        }

        var retained = discovery.Files.Where(probe =>
        {
            var extension = Path.GetExtension(probe.FullPath).ToLowerInvariant();
            return probe.SizeInBytes <= files.MaximumFileSizeBytes &&
                   (files.IncludedFileTypes.Count == 0 ||
                    files.IncludedFileTypes.Contains(extension, StringComparer.OrdinalIgnoreCase)) &&
                   !files.ExcludedFileTypes.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
                   (files.IncludeHiddenFiles || (probe.Attributes & FileAttributes.Hidden) == 0);
        }).ToArray();
        return discovery with
        {
            Files = Array.AsReadOnly(retained),
            Ignored = discovery.Ignored + discovery.Files.Count - retained.Length,
        };
    }

    private static void RemoveByPath(Dictionary<string, WatchedFileProbe> desired, string path)
    {
        var key = desired.FirstOrDefault(pair =>
            WatchedFolderPathPolicy.PathComparer.Equals(pair.Value.FullPath, path)).Key;
        if (key is not null)
        {
            desired.Remove(key);
        }
    }

    private static WatchedFileProbe ToProbe(WatchedFileState state) => new(
        state.FullPath,
        false,
        state.SizeInBytes,
        state.CreationTimeUtc,
        state.LastWriteTimeUtc,
        state.Attributes,
        state.StableId);

    private static FileEntry ToFileEntry(WatchedFileState state) => new(
        state.FullPath,
        new FileMetadata(
            Path.GetFileName(state.FullPath),
            Path.GetExtension(state.FullPath).ToLowerInvariant(),
            state.SizeInBytes,
            state.CreationTimeUtc,
            state.LastWriteTimeUtc,
            null,
            state.Attributes),
        string.IsNullOrWhiteSpace(state.ContentHash) ? null : new FileHash("SHA-256", state.ContentHash),
        state.Category is null ? null : new FileClassification(state.Category.Value),
        new DuplicateClassification(state.DuplicateStatus, state.DuplicateGroupId));

    private static WatchedFileState CreatePreservedState(
        FileWorkItem item,
        DateTimeOffset now,
        WatchedScanReason reason)
    {
        var previous = item.Previous ??
                       throw new InvalidOperationException("Only existing unchanged items may preserve analysis.");
        return previous with
        {
            FullPath = item.Probe.FullPath,
            SizeInBytes = item.Probe.SizeInBytes,
            CreationTimeUtc = item.Probe.CreationTimeUtc,
            LastWriteTimeUtc = item.Probe.LastWriteTimeUtc,
            Attributes = item.Probe.Attributes,
            AnalysedAtUtc = item.IsMetadataChanged ? now : previous.AnalysedAtUtc,
            LastReprocessReason = item.IsMetadataChanged || item.IsRenamedOrMoved
                ? DetermineReason(item, reason)
                : previous.LastReprocessReason,
        };
    }

    private static WatchedItemReprocessReason DetermineReason(
        FileWorkItem item,
        WatchedScanReason reason) =>
        reason == WatchedScanReason.OpenSorSeExecution
            ? WatchedItemReprocessReason.OpenSorSeExecution
            : item.IsNew
                ? WatchedItemReprocessReason.Discovered
                : item.IsRenamedOrMoved
                    ? WatchedItemReprocessReason.RenamedOrMoved
                    : item.IsContentChanged
                        ? WatchedItemReprocessReason.ContentChanged
                        : item.IsMetadataChanged
                            ? WatchedItemReprocessReason.MetadataChanged
                            : WatchedItemReprocessReason.Reconciliation;

    private static string FindStableId(IEnumerable<WatchedFileState> states, string path) =>
        states.First(state => WatchedFolderPathPolicy.PathComparer.Equals(state.FullPath, path)).StableId;

    private static bool IsReconciliationReason(WatchedScanReason reason) => reason is
        WatchedScanReason.UserFullReconciliation or
        WatchedScanReason.StartupOfflineReconciliation or
        WatchedScanReason.ResumeReconciliation or
        WatchedScanReason.OverflowRecovery or
        WatchedScanReason.ReconnectReconciliation or
        WatchedScanReason.ConfigurationChangedReconciliation;

    private static void ValidateExistingScope(
        WatchedFolderConfiguration configuration,
        WatchedFolderCatalogue? existing)
    {
        if (existing is null)
        {
            return;
        }

        if (!string.Equals(existing.ConfigurationId, configuration.Id, StringComparison.Ordinal) ||
            !WatchedFolderPathPolicy.PathComparer.Equals(
                Path.GetFullPath(existing.RootPath),
                Path.GetFullPath(configuration.FolderPath)))
        {
            throw new InvalidDataException("The watched catalogue scope does not match its configuration.");
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record DiscoveryResult(
        IReadOnlyList<WatchedFileProbe> Files,
        IReadOnlyList<WatchedDirectoryState> Directories,
        int Ignored);

    private sealed record FileWorkItem(
        WatchedFileProbe Probe,
        WatchedFileState? Previous,
        bool IsNew,
        bool IsRenamedOrMoved,
        bool IsContentChanged,
        bool IsMetadataChanged)
    {
        public bool RequiresContentProcessing =>
            IsNew ||
            IsContentChanged ||
            Previous?.LastReprocessReason == WatchedItemReprocessReason.DeferredUntilStable;
    }

    private sealed record ComparisonResult(
        IReadOnlyList<FileWorkItem> Current,
        int Added,
        int Updated,
        int RenamedOrMoved,
        int Removed);

    private sealed record PlanningResult(
        IReadOnlyList<PlannedOperation> Operations,
        IReadOnlyList<string> Warnings);
}
