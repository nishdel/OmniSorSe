using System.Diagnostics;
using System.Data.Common;
using System.Globalization;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Semantic;

/// <summary>
/// Interprets, filters, and ranks bounded local Search candidates without requiring AI.
/// Precise lexical tiers always precede optional related-concept similarity.
/// </summary>
public sealed class SemanticSearchService : ISemanticSearchService
{
    private const int MaximumConcurrentQueries = 4;
    private readonly IConfigurationService _configurationService;
    private readonly IDiagnosticsEventSink? _diagnostics;
    private readonly ISemanticIndexStore _indexStore;
    private readonly IProgressiveSearchSource? _progressiveSearchSource;
    private readonly IProgressiveSearchDocumentLookup? _searchDocumentLookup;
    private readonly IRelationshipSearchSource? _relationshipSearchSource;
    private readonly IGraphSearchSource? _graphSearchSource;
    private readonly SemaphoreSlim _queryGate = new(MaximumConcurrentQueries, MaximumConcurrentQueries);
    private readonly ISearchQueryInterpreter _queryInterpreter;
    private readonly ISearchRanker _ranker;

    /// <summary>Initializes the bounded local hybrid Search service.</summary>
    public SemanticSearchService(
        IConfigurationService configurationService,
        IEmbeddingProvider embeddingProvider,
        ISemanticIndexStore indexStore,
        IProgressiveSearchSource? progressiveSearchSource = null,
        ISearchQueryInterpreter? queryInterpreter = null,
        ISearchRanker? ranker = null,
        ISearchSnippetFactory? snippetFactory = null,
        IDiagnosticsEventSink? diagnostics = null,
        IRelationshipSearchSource? relationshipSearchSource = null,
        IGraphSearchSource? graphSearchSource = null,
        IProgressiveSearchDocumentLookup? searchDocumentLookup = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _progressiveSearchSource = progressiveSearchSource;
        _queryInterpreter = queryInterpreter ?? new DeterministicSearchQueryInterpreter();
        var snippets = snippetFactory ?? new SearchSnippetFactory();
        _ranker = ranker ?? new HybridSearchRanker(embeddingProvider, snippets);
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
        _relationshipSearchSource = relationshipSearchSource;
        _graphSearchSource = graphSearchSource;
        _searchDocumentLookup = searchDocumentLookup ?? progressiveSearchSource as IProgressiveSearchDocumentLookup;
    }

    /// <inheritdoc />
    public async Task<SemanticResult<IReadOnlyList<SemanticSearchHit>>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var advanced = await SearchAsync(new SearchRequest(query), cancellationToken).ConfigureAwait(false);
        return new SemanticResult<IReadOnlyList<SemanticSearchHit>>(
            advanced.State,
            advanced.Message,
            advanced.Hits);
    }

    /// <inheritdoc />
    public async Task<SearchExecutionResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = _configurationService.Current.SemanticSearch;
        if (!settings.Enabled)
        {
            return Result(
                SemanticState.Disabled,
                "Search is disabled in Settings.",
                [],
                EmptyInterpretation(request.QueryText),
                EmptyCoverage);
        }

        SearchInterpretation interpretation;
        try
        {
            interpretation = _queryInterpreter.Interpret(request);
        }
        catch (SearchQueryValidationException exception)
        {
            return Result(
                SemanticState.Failed,
                exception.Message,
                [],
                EmptyInterpretation(request.QueryText),
                EmptyCoverage);
        }

        var started = Stopwatch.GetTimestamp();
        var session = _diagnostics?.BeginSession(
            DiagnosticCategory.SearchAndIndexing,
            "Local Search query",
            [
                new DiagnosticField(
                    "Query length",
                    (request.QueryText?.Length ?? 0).ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField(
                    "Topic token count",
                    interpretation.TopicTokens.Count.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticField(
                    "Filter count",
                    interpretation.Filters.Count.ToString(CultureInfo.InvariantCulture)),
            ]);
        try
        {
            await _queryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var legacyTask = LoadLegacySafelyAsync(cancellationToken);
                var progressiveTask = LoadProgressiveSafelyAsync(
                    settings.MaximumDocumentCount,
                    cancellationToken);
                await Task.WhenAll(legacyTask, progressiveTask).ConfigureAwait(false);
                var progressive = await progressiveTask.ConfigureAwait(false);
                var coverage = progressive.Coverage;
                var candidates = MergeCandidates(
                    await legacyTask.ConfigureAwait(false),
                    progressive.Documents,
                    progressive.ExcludedPaths,
                    settings.MaximumDocumentCount);
                if (candidates.Count == 0)
                {
                    var emptyGraph = request.IncludeGraphContext && _graphSearchSource is not null
                        ? await LoadGraphExpansionsSafelyAsync([], cancellationToken).ConfigureAwait(false)
                        : null;
                    var emptyMessage = CoverageMessage(coverage, hasResults: false);
                    CompleteDiagnostics(
                        session,
                        DiagnosticStatus.Succeeded,
                        started,
                        0,
                        interpretation,
                        coverage,
                        "No bounded local candidate was available.",
                        emptyGraph?.Coverage);
                    return Result(
                        SemanticState.Empty,
                        emptyMessage,
                        [],
                        interpretation,
                        coverage,
                        emptyGraph?.Coverage);
                }

                var ranked = _ranker.Rank(
                    interpretation,
                    candidates,
                    settings.MaximumResultCount,
                    cancellationToken);
                GraphProjectionCoverage? graphCoverage = null;
                if ((request.IncludeRelationshipContext && _relationshipSearchSource is not null) ||
                    (request.IncludeGraphContext && _graphSearchSource is not null))
                {
                    var seedIds = ranked
                        .Select(item => item.Document.FileId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Cast<string>()
                        .Distinct(StringComparer.Ordinal)
                        .Take(GraphLimits.MaximumSearchSeeds)
                        .ToArray();

                    var relationshipExpansions = request.IncludeRelationshipContext
                        ? await LoadRelationshipExpansionsSafelyAsync(seedIds, cancellationToken).ConfigureAwait(false)
                        : [];
                    var graphResult = request.IncludeGraphContext
                        ? await LoadGraphExpansionsSafelyAsync(seedIds, cancellationToken).ConfigureAwait(false)
                        : null;
                    graphCoverage = graphResult?.Coverage;

                    var seedSet = seedIds.ToHashSet(StringComparer.Ordinal);
                    var relationshipContexts = relationshipExpansions
                        .Where(item => !seedSet.Contains(item.RelatedFileId))
                        .GroupBy(item => item.RelatedFileId, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Take(GraphLimits.MaximumContextualSearchExpansions)
                        .ToDictionary(
                            group => group.Key,
                            group => group
                                .OrderByDescending(item => item.Confidence)
                                .ThenBy(item => item.Type)
                                .ThenBy(item => item.SeedFileId, StringComparer.Ordinal)
                                .Select(item => new SearchRelationshipContext(
                                    item.SeedFileId,
                                    item.Type,
                                    item.Confidence,
                                    item.Explanation,
                                    item.CollectionTitle))
                                .First(),
                            StringComparer.Ordinal);
                    var graphLimit = Math.Min(
                        GraphLimits.MaximumGraphSearchExpansions,
                        GraphLimits.MaximumContextualSearchExpansions - relationshipContexts.Count);
                    var graphContexts = (graphResult?.IsAvailable == true
                            ? graphResult.Expansions
                            : [])
                        .Where(item =>
                            item.Freshness == GraphFreshnessState.Current &&
                            !seedSet.Contains(item.RelatedFileId) &&
                            !relationshipContexts.ContainsKey(item.RelatedFileId) &&
                            !string.IsNullOrWhiteSpace(item.EdgeId) &&
                            !string.IsNullOrWhiteSpace(item.Explanation))
                        .GroupBy(item => item.RelatedFileId, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Take(graphLimit)
                        .ToDictionary(
                            group => group.Key,
                            group => group
                                .OrderByDescending(item => item.Confidence)
                                .ThenBy(item => item.EdgeKind.Value, StringComparer.Ordinal)
                                .ThenBy(item => item.SeedFileId, StringComparer.Ordinal)
                                .Select(item => new SearchGraphContext(
                                    item.SeedFileId,
                                    item.EdgeId,
                                    item.EdgeKind,
                                    item.Confidence,
                                    item.Explanation,
                                    item.ProjectionRevision,
                                    item.Freshness))
                                .First(),
                            StringComparer.Ordinal);

                    if (relationshipContexts.Count > 0 || graphContexts.Count > 0)
                    {
                        candidates = await AddContextTargetsAsync(
                                candidates,
                                relationshipContexts.Keys.Concat(graphContexts.Keys),
                                cancellationToken)
                            .ConfigureAwait(false);
                        var contextualCandidates = candidates
                            .Select(candidate => candidate.FileId is not null &&
                                relationshipContexts.TryGetValue(candidate.FileId, out var relationshipContext)
                                    ? candidate with { RelationshipContext = relationshipContext, GraphContext = null }
                                    : candidate.FileId is not null &&
                                      graphContexts.TryGetValue(candidate.FileId, out var graphContext)
                                        ? candidate with { GraphContext = graphContext }
                                        : candidate)
                            .ToArray();
                        ranked = _ranker.Rank(
                            interpretation,
                            contextualCandidates,
                            settings.MaximumResultCount,
                            cancellationToken);
                    }
                }
                var hits = ranked.Select(ToHit).ToArray();
                var message = CoverageMessage(coverage, hits.Length > 0);
                CompleteDiagnostics(
                    session,
                    DiagnosticStatus.Succeeded,
                    started,
                    hits.Length,
                    interpretation,
                    coverage,
                    "Bounded local ranking completed.",
                    graphCoverage);
                return Result(
                    hits.Length == 0 ? SemanticState.Empty : SemanticState.Ready,
                    message,
                    Array.AsReadOnly(hits),
                    interpretation,
                    coverage,
                    graphCoverage);
            }
            finally
            {
                _queryGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CompleteDiagnostics(
                session,
                DiagnosticStatus.Cancelled,
                started,
                0,
                interpretation,
                EmptyCoverage,
                "The local Search query was cancelled.");
            return Result(
                SemanticState.Cancelled,
                "Search was cancelled.",
                [],
                interpretation,
                EmptyCoverage);
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException)
        {
            _diagnostics?.Complete(
                session,
                DiagnosticStatus.Failed,
                Stopwatch.GetElapsedTime(started),
                "Search failed safely while reading or ranking the local index.",
                DiagnosticSeverity.Error,
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            return Result(
                SemanticState.Failed,
                "The local Search index is temporarily unavailable. Filename and metadata coverage will return after recovery or rebuild.",
                [],
                interpretation,
                EmptyCoverage);
        }
    }

    private async Task<IReadOnlyList<SemanticIndexEntry>> LoadLegacySafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _indexStore.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableIndexFailure(exception))
        {
            _diagnostics?.Publish(
                null,
                "Legacy Search index fallback",
                DiagnosticStatus.PartiallySucceeded,
                DiagnosticSeverity.Warning,
                DiagnosticSection.Performance,
                "The compatible legacy Search store was unavailable; progressive filename and metadata Search continued.",
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            return [];
        }
    }

    private async Task<ProgressiveLoad> LoadProgressiveSafelyAsync(
        int maximumDocuments,
        CancellationToken cancellationToken)
    {
        if (_progressiveSearchSource is null)
        {
            return new ProgressiveLoad([], EmptyCoverage, []);
        }

        try
        {
            var documentsTask = _progressiveSearchSource.GetDocumentsAsync(
                maximumDocuments,
                cancellationToken);
            var coverageTask = _progressiveSearchSource.GetCoverageAsync(cancellationToken);
            var exclusionsTask = _progressiveSearchSource.GetExcludedPathsAsync(
                maximumDocuments,
                cancellationToken);
            await Task.WhenAll(documentsTask, coverageTask, exclusionsTask).ConfigureAwait(false);
            return new ProgressiveLoad(
                await documentsTask.ConfigureAwait(false),
                await coverageTask.ConfigureAwait(false),
                await exclusionsTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableIndexFailure(exception))
        {
            _diagnostics?.Publish(
                null,
                "Deep Search index fallback",
                DiagnosticStatus.PartiallySucceeded,
                DiagnosticSeverity.Warning,
                DiagnosticSection.Performance,
                "The deep Search store was unavailable; compatible filename and metadata Search continued.",
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            return new ProgressiveLoad(
                [],
                EmptyCoverage with { IsAvailable = false },
                []);
        }
    }

    private async Task<IReadOnlyList<RelationshipSearchExpansion>> LoadRelationshipExpansionsSafelyAsync(
        IReadOnlyList<string> seedFileIds,
        CancellationToken cancellationToken)
    {
        if (_relationshipSearchSource is null || seedFileIds.Count == 0)
        {
            return [];
        }

        try
        {
            return await _relationshipSearchSource
                .ExpandAsync(seedFileIds, RelationshipLimits.MaximumSearchExpansions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableIndexFailure(exception))
        {
            _diagnostics?.Publish(
                null,
                "Relationship Search fallback",
                DiagnosticStatus.PartiallySucceeded,
                DiagnosticSeverity.Warning,
                DiagnosticSection.Performance,
                "Relationship context was unavailable; exact and ordinary local Search continued.",
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            return [];
        }
    }

    private async Task<GraphSearchResult?> LoadGraphExpansionsSafelyAsync(
        IReadOnlyList<string> seedFileIds,
        CancellationToken cancellationToken)
    {
        if (_graphSearchSource is null)
        {
            return null;
        }

        try
        {
            var result = await _graphSearchSource
                .ExpandAsync(
                    new GraphSearchRequest(
                        seedFileIds.Take(GraphLimits.MaximumSearchSeeds).ToArray(),
                        GraphLimits.MaximumGraphSearchExpansions),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result is null || result.Coverage is null || result.Expansions is null)
            {
                throw new InvalidDataException("The graph Search provider returned an incomplete result contract.");
            }

            var bounded = result.Expansions
                .Take(GraphLimits.MaximumGraphSearchExpansions)
                .ToArray();
            return result with { Expansions = Array.AsReadOnly(bounded) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics?.Publish(
                null,
                "Knowledge Graph Search fallback",
                DiagnosticStatus.PartiallySucceeded,
                DiagnosticSeverity.Warning,
                DiagnosticSection.Performance,
                "Knowledge Graph context was unavailable; exact, ordinary, and direct relationship Search continued.",
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            const string message = "Knowledge Graph context is temporarily unavailable. Ordinary Search remains available.";
            return new GraphSearchResult(
                [],
                new GraphProjectionCoverage(
                    true,
                    false,
                    false,
                    false,
                    0,
                    0,
                    0,
                    0,
                    null,
                    0,
                    message),
                false,
                message);
        }
    }

    private async Task<IReadOnlyList<SearchCandidateDocument>> AddContextTargetsAsync(
        IReadOnlyList<SearchCandidateDocument> candidates,
        IEnumerable<string> targetFileIds,
        CancellationToken cancellationToken)
    {
        if (_searchDocumentLookup is null)
        {
            return candidates;
        }

        var existingIds = candidates
            .Select(candidate => candidate.FileId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var requested = targetFileIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !existingIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Take(GraphLimits.MaximumContextualSearchExpansions)
            .ToArray();
        if (requested.Length == 0)
        {
            return candidates;
        }

        try
        {
            var requestedSet = requested.ToHashSet(StringComparer.Ordinal);
            var resolved = await _searchDocumentLookup
                .GetDocumentsByIdsAsync(requested, cancellationToken)
                .ConfigureAwait(false);
            var additions = resolved
                .Where(document =>
                    !document.IsExcluded &&
                    requestedSet.Contains(document.FileId) &&
                    !existingIds.Contains(document.FileId))
                .GroupBy(document => document.FileId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => FromProgressive(group.First()))
                .ToArray();
            return Array.AsReadOnly(
                candidates
                    .Concat(additions)
                    .OrderBy(candidate => candidate.FullPath, PathComparer)
                    .ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics?.Publish(
                null,
                "Context target lookup fallback",
                DiagnosticStatus.PartiallySucceeded,
                DiagnosticSeverity.Warning,
                DiagnosticSection.Performance,
                "Context target details were unavailable; already-loaded Search results continued.",
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            return candidates;
        }
    }

    private IReadOnlyList<SearchCandidateDocument> MergeCandidates(
        IReadOnlyList<SemanticIndexEntry> legacyEntries,
        IReadOnlyList<ProgressiveSearchDocument> progressiveDocuments,
        IReadOnlyList<string> excludedPaths,
        int maximumDocuments)
    {
        var byPath = new Dictionary<string, SearchCandidateDocument>(PathComparer);
        foreach (var entry in legacyEntries.OrderBy(item => item.FullPath, PathComparer))
        {
            byPath[entry.FullPath] = FromLegacy(entry);
        }

        foreach (var document in progressiveDocuments.OrderBy(item => item.FullPath, PathComparer))
        {
            if (document.IsExcluded)
            {
                byPath.Remove(document.FullPath);
                continue;
            }

            var progressive = FromProgressive(document);
            if (byPath.TryGetValue(progressive.FullPath, out var legacy))
            {
                progressive = progressive with
                {
                    Tags = legacy.Tags,
                    SemanticRepresentation = progressive.SemanticRepresentation is { Count: > 0 }
                        ? progressive.SemanticRepresentation
                        : legacy.SemanticRepresentation,
                };
            }

            byPath[progressive.FullPath] = progressive;
        }

        foreach (var excludedPath in excludedPaths)
        {
            byPath.Remove(excludedPath);
        }

        return Array.AsReadOnly(
            byPath.Values
                .OrderBy(item => item.FullPath, PathComparer)
                .Take(maximumDocuments)
                .ToArray());
    }

    private static SearchCandidateDocument FromLegacy(SemanticIndexEntry entry)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(entry.FullPath)) ?? string.Empty;
        var extension = Path.GetExtension(entry.FileName).ToLowerInvariant();
        return new SearchCandidateDocument
        {
            FullPath = entry.FullPath,
            FileName = entry.FileName,
            RelativePath = entry.FullPath,
            FolderName = folder,
            Extension = extension,
            FileType = SearchFileTypeClassifier.Classify(extension),
            Tags = Array.AsReadOnly(
                entry.Tags
                    .Where(tag => tag.AcceptanceState == TagAcceptanceState.Accepted)
                    .Select(tag => tag.DisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()),
            MetadataText = string.Join(' ', entry.MetadataTerms),
            ExtractedText = string.Join(' ', entry.NativeTextTerms),
            OcrText = string.Join(' ', entry.OcrTextTerms),
            SemanticRepresentation = entry.Vector,
            ModifiedTimeUtc = entry.IndexedAtUtc,
            IsFullyIndexed = true,
        };
    }

    private static SearchCandidateDocument FromProgressive(ProgressiveSearchDocument document)
    {
        var extension = string.IsNullOrWhiteSpace(document.Extension)
            ? Path.GetExtension(document.FileName).ToLowerInvariant()
            : document.Extension.ToLowerInvariant();
        return new SearchCandidateDocument
        {
            FileId = document.FileId,
            FullPath = document.FullPath,
            FileName = document.FileName,
            RelativePath = document.RelativePath,
            FolderName = document.FolderName,
            Extension = extension,
            FileType = string.IsNullOrWhiteSpace(document.FileType)
                ? SearchFileTypeClassifier.Classify(extension)
                : document.FileType,
            SourceId = document.SourceId,
            SourceName = document.SourceName,
            SourcePriority = document.SourcePriority,
            Length = document.Length,
            CreationTimeUtc = document.CreationTimeUtc,
            ModifiedTimeUtc = document.ModifiedTimeUtc,
            IndexingLevel = document.IndexingLevel,
            Tags = document.Tags,
            MetadataText = document.MetadataText,
            ExtractedText = document.ExtractedText,
            OcrText = document.OcrText,
            Summary = document.Summary,
            Keywords = document.Keywords,
            Chunks = document.SelectedChunks,
            SemanticRepresentation = document.SemanticRepresentation,
            IsFullyIndexed = document.IsFullyIndexed,
            HasIndexingFailure = document.HasIndexingFailure,
        };
    }

    private static SemanticSearchHit ToHit(RankedSearchCandidate candidate)
    {
        var components = candidate.Components;
        return new SemanticSearchHit(
            candidate.Document.FullPath,
            candidate.Document.FileName,
            candidate.Score,
            string.Join(
                "; ",
                components
                    .Where(component => component.Kind is not
                        SearchRankingSignalKind.Recency and not
                        SearchRankingSignalKind.SourcePriority)
                    .Select(component => component.Explanation)
                    .Distinct(StringComparer.Ordinal)),
            Array.AsReadOnly(
                candidate.Document.Tags
                    .Where(tag => components.Any(component =>
                        component.Kind == SearchRankingSignalKind.Tag &&
                        (component.MatchedText is null ||
                         tag.Contains(component.MatchedText, StringComparison.OrdinalIgnoreCase))))
                    .ToArray()),
            components.Any(component => component.Kind == SearchRankingSignalKind.Metadata),
            components.Any(component => component.Kind == SearchRankingSignalKind.ExtractedText),
            components.Any(component => component.Kind == SearchRankingSignalKind.OcrText),
            candidate.Document.FileId,
            components,
            candidate.Snippet,
            candidate.Document.IsFullyIndexed,
            candidate.Document.IndexingLevel,
            candidate.Document.SourceName);
    }

    private static string CoverageMessage(SearchCoverage coverage, bool hasResults)
    {
        if (!coverage.IsAvailable)
        {
            return "The deep Search index is temporarily unavailable. Existing filename and metadata Search remains available.";
        }

        var prefix = hasResults ? "Local results are ready." : "No local match was found.";
        if (coverage.KnownFileCount == 0 &&
            coverage.ExcludedSourceCount == 0 &&
            coverage.WaitingForOcrCount == 0 &&
            coverage.WaitingForAiCount == 0 &&
            coverage.FailedStageCount == 0)
        {
            return $"{prefix} The background Search index is empty.";
        }

        if (coverage.IsIncomplete ||
            coverage.ExcludedSourceCount > 0 ||
            coverage.WaitingForOcrCount > 0 ||
            coverage.WaitingForAiCount > 0 ||
            coverage.FailedStageCount > 0)
        {
            var reasons = new List<string>();
            if (coverage.IsIncomplete)
            {
                reasons.Add("indexing is incomplete");
            }

            if (coverage.ExcludedSourceCount > 0)
            {
                reasons.Add("some sources or files are excluded");
            }

            if (coverage.WaitingForOcrCount > 0)
            {
                reasons.Add("OCR work is waiting");
            }

            if (coverage.WaitingForAiCount > 0)
            {
                reasons.Add("optional local-AI work is waiting");
            }

            if (coverage.FailedStageCount > 0)
            {
                reasons.Add("some indexing stages failed");
            }

            return $"{prefix} Results may be incomplete because {string.Join(", ", reasons)}.";
        }

        return $"{prefix} All known files have complete indexing coverage.";
    }

    private void CompleteDiagnostics(
        string? session,
        DiagnosticStatus status,
        long started,
        int resultCount,
        SearchInterpretation interpretation,
        SearchCoverage coverage,
        string summary,
        GraphProjectionCoverage? graphCoverage = null)
    {
        var fields = new List<DiagnosticField>
        {
            new("Result count", resultCount.ToString(CultureInfo.InvariantCulture)),
            new("Ranking stages", "filters, literal tiers, bounded typo matching, optional relationship context, optional Knowledge Graph context, related concepts, stable tie-breakers"),
            new("Filter count", interpretation.Filters.Count.ToString(CultureInfo.InvariantCulture)),
            new("Known file coverage", coverage.KnownFileCount.ToString(CultureInfo.InvariantCulture)),
            new("Fully indexed coverage", coverage.FullyIndexedCount.ToString(CultureInfo.InvariantCulture)),
            new("Index available", coverage.IsAvailable.ToString(CultureInfo.InvariantCulture)),
        };
        if (graphCoverage is not null)
        {
            fields.Add(new DiagnosticField(
                "Graph available",
                graphCoverage.IsAvailable.ToString(CultureInfo.InvariantCulture)));
            fields.Add(new DiagnosticField(
                "Graph projection revision",
                graphCoverage.ProjectionRevision.ToString(CultureInfo.InvariantCulture)));
            fields.Add(new DiagnosticField(
                "Graph projected observations",
                graphCoverage.ProjectedObservationCount.ToString(CultureInfo.InvariantCulture)));
        }

        _diagnostics?.Complete(
            session,
            status,
            Stopwatch.GetElapsedTime(started),
            summary,
            status == DiagnosticStatus.Failed ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
            fields);
    }

    private static SearchExecutionResult Result(
        SemanticState state,
        string message,
        IReadOnlyList<SemanticSearchHit> hits,
        SearchInterpretation interpretation,
        SearchCoverage coverage,
        GraphProjectionCoverage? graphCoverage = null) =>
        new SearchExecutionResult(state, message, hits, interpretation, coverage)
        {
            GraphCoverage = graphCoverage,
        };

    private static SearchInterpretation EmptyInterpretation(string? query) => new(
        query ?? string.Empty,
        query?.Trim() ?? string.Empty,
        [],
        []);

    private static SearchCoverage EmptyCoverage => new(0, 0, 0, 0, 0, 0);

    private static bool IsRecoverableIndexFailure(Exception exception) =>
        exception is IOException or
            DbException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            TimeoutException;

    private static StringComparer PathComparer =>
        OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparer;

    private sealed record ProgressiveLoad(
        IReadOnlyList<ProgressiveSearchDocument> Documents,
        SearchCoverage Coverage,
        IReadOnlyList<string> ExcludedPaths);
}
