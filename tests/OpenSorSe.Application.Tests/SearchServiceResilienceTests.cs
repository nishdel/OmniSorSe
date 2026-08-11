using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies local Search degradation, concurrency, exclusions, cancellation, and diagnostic privacy.</summary>
public sealed class SearchServiceResilienceTests
{
    private static readonly FeatureHashingEmbeddingProvider Embeddings = new();

    /// <summary>Verifies a deep-index failure preserves compatible filename and metadata Search.</summary>
    [Fact]
    public async Task ProgressiveFailureFallsBackToLegacySearch()
    {
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([Legacy("fallback.pdf")]),
            new Progressive { Failure = new IOException("synthetic") });

        var result = await service.SearchAsync(new SearchRequest("fallback.pdf"), CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal("fallback.pdf", Assert.Single(result.Hits).FileName);
        Assert.False(result.Coverage.IsAvailable);
        Assert.Contains("temporarily unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a compatible legacy-store failure preserves progressive filename Search.</summary>
    [Fact]
    public async Task LegacyFailureFallsBackToProgressiveSearch()
    {
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([], new InvalidDataException("synthetic")),
            new Progressive
            {
                Documents =
                [
                    Document("deep", "deep-report.pdf"),
                ],
                Coverage = new SearchCoverage(1, 1, 0, 0, 0, 0),
            });

        var result = await service.SearchAsync(new SearchRequest("deep-report.pdf"), CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal("deep", Assert.Single(result.Hits).FileId);
    }

    /// <summary>Verifies an index-forget exclusion suppresses a matching compatible legacy entry.</summary>
    [Fact]
    public async Task DurableExclusionPathSuppressesLegacySearchEntry()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OpenSorSe-synthetic", "forgotten.pdf"));
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([Legacy("forgotten.pdf", path)]),
            new Progressive
            {
                ExcludedPaths = [path],
                Coverage = new SearchCoverage(0, 0, 0, 0, 0, 0)
                {
                    ExcludedSourceCount = 1,
                },
            });

        var result = await service.SearchAsync(new SearchRequest("forgotten.pdf"), CancellationToken.None);

        Assert.Empty(result.Hits);
        Assert.Contains("excluded", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies no-result messaging distinguishes dependency waits and incomplete coverage.</summary>
    [Fact]
    public async Task NoResultExplainsIncompleteDependencyCoverage()
    {
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([]),
            new Progressive
            {
                Documents = [Document("other", "other.txt")],
                Coverage = new SearchCoverage(5, 5, 2, 0, 0, 1)
                {
                    WaitingForOcrCount = 1,
                    WaitingForAiCount = 1,
                    FailedStageCount = 1,
                },
            });

        var result = await service.SearchAsync(new SearchRequest("not-present"), CancellationToken.None);

        Assert.Empty(result.Hits);
        Assert.Equal(SemanticState.Empty, result.State);
        Assert.Contains("No local match", result.Message, StringComparison.Ordinal);
        Assert.Contains("OCR", result.Message, StringComparison.Ordinal);
        Assert.Contains("local-AI", result.Message, StringComparison.Ordinal);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies complete query text and snippets never enter default Search diagnostics.</summary>
    [Fact]
    public async Task DiagnosticsRecordShapeAndTimingButNotPrivateQueryText()
    {
        const string privateQuery = "private-tax-token-9843";
        var diagnostics = new DiagnosticsSink();
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([Legacy("private-tax-token-9843.txt")]),
            diagnostics: diagnostics);

        _ = await service.SearchAsync(new SearchRequest(privateQuery), CancellationToken.None);

        var retained = string.Join(
            " ",
            diagnostics.Values.Concat(diagnostics.Messages).Concat(diagnostics.Operations));
        Assert.DoesNotContain(privateQuery, retained, StringComparison.Ordinal);
        Assert.Contains("Query length", retained, StringComparison.Ordinal);
        Assert.Contains("Result count", retained, StringComparison.Ordinal);
        Assert.Contains("Ranking stages", retained, StringComparison.Ordinal);
    }

    /// <summary>Verifies overlapping queries are capped without arbitrary sleeps.</summary>
    [Fact]
    public async Task ConcurrentSearchRequestsAreBounded()
    {
        var progressive = new ControlledProgressive();
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([]),
            progressive);
        var searches = Enumerable.Range(0, 8)
            .Select(index => service.SearchAsync(
                new SearchRequest($"query-{index}"),
                CancellationToken.None))
            .ToArray();

        await progressive.FourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, progressive.MaximumActive);
        progressive.Release.TrySetResult();
        var results = await Task.WhenAll(searches);

        Assert.All(results, result => Assert.NotEqual(SemanticState.Failed, result.State));
        Assert.Equal(4, progressive.MaximumActive);
    }

    /// <summary>Verifies cancellation while waiting for bounded query capacity returns a clean cancelled result.</summary>
    [Fact]
    public async Task WaitingQueryCancellationReturnsNoPartialResults()
    {
        var progressive = new ControlledProgressive();
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([]),
            progressive);
        var blockers = Enumerable.Range(0, 4)
            .Select(index => service.SearchAsync(new SearchRequest($"block-{index}"), CancellationToken.None))
            .ToArray();
        await progressive.FourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var waiting = service.SearchAsync(new SearchRequest("cancel-me"), cancellation.Token);
        cancellation.Cancel();

        var result = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        progressive.Release.TrySetResult();
        await Task.WhenAll(blockers);

        Assert.Equal(SemanticState.Cancelled, result.State);
        Assert.Empty(result.Hits);
    }

    /// <summary>Verifies the service invokes the optional assistant only for an explicit request.</summary>
    [Fact]
    public async Task ExplicitAiAssistanceUsesOnlyRankedLocalCandidates()
    {
        var assistant = new RecordingAssistant();
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([Legacy("report-one.txt"), Legacy("report-two.txt")]),
            aiSearchAssistant: assistant);

        var result = await service.SearchAsync(
            new SearchRequest("report") { UseAiAssistance = true },
            CancellationToken.None);

        Assert.Equal(AiSearchAssistanceState.Applied, result.AiAssistance.State);
        Assert.Equal(2, assistant.Candidates.Count);
        Assert.Equal(
            assistant.Candidates.Reverse().Select(item => item.Document.FullPath),
            result.Hits.Select(item => item.FullPath));
    }

    /// <summary>Verifies missing optional AI composition never prevents deterministic Search.</summary>
    [Fact]
    public async Task MissingAiAssistantFallsBackWithoutLosingResults()
    {
        var service = new SemanticSearchService(
            new Configuration(),
            Embeddings,
            new Store([Legacy("fallback-report.txt")]));

        var result = await service.SearchAsync(
            new SearchRequest("fallback-report") { UseAiAssistance = true },
            CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Single(result.Hits);
        Assert.Equal(AiSearchAssistanceState.Unavailable, result.AiAssistance.State);
    }

    private static SemanticIndexEntry Legacy(string fileName, string? fullPath = null) => new(
        fullPath ?? Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OpenSorSe-synthetic", fileName)),
        "source",
        "index",
        fileName,
        [],
        [],
        [],
        [],
        Embeddings.Embed(fileName),
        DateTimeOffset.UnixEpoch);

    private static ProgressiveSearchDocument Document(
        string id,
        string fileName,
        string? fullPath = null) => new()
        {
            FileId = id,
            FullPath = fullPath ?? Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OpenSorSe-synthetic", fileName)),
            FileName = fileName,
            RelativePath = fileName,
            FolderName = "OpenSorSe-synthetic",
        };

    private sealed class Configuration : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            SemanticSearch = new SemanticSearchSettings
            {
                Enabled = true,
                MaximumDocumentCount = 100,
                MaximumResultCount = 20,
            },
        };

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Store(
        IReadOnlyList<SemanticIndexEntry> entries,
        Exception? failure = null) : ISemanticIndexStore
    {
        public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken) =>
            failure is null
                ? Task.FromResult(entries)
                : Task.FromException<IReadOnlyList<SemanticIndexEntry>>(failure);

        public Task ReplaceAsync(
            IReadOnlyList<SemanticIndexEntry> replacement,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private class Progressive : IProgressiveSearchSource
    {
        public IReadOnlyList<ProgressiveSearchDocument> Documents { get; init; } = [];

        public SearchCoverage Coverage { get; init; } = new(0, 0, 0, 0, 0, 0);

        public IReadOnlyList<string> ExcludedPaths { get; init; } = [];

        public Exception? Failure { get; init; }

        public virtual Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Failure is null
                ? Task.FromResult(Documents)
                : Task.FromException<IReadOnlyList<ProgressiveSearchDocument>>(Failure);

        public virtual Task<SearchCoverage> GetCoverageAsync(
            CancellationToken cancellationToken = default) =>
            Failure is null
                ? Task.FromResult(Coverage)
                : Task.FromException<SearchCoverage>(Failure);

        public virtual Task<IReadOnlyList<string>> GetExcludedPathsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Failure is null
                ? Task.FromResult(ExcludedPaths)
                : Task.FromException<IReadOnlyList<string>>(Failure);
    }

    private sealed class ControlledProgressive : Progressive
    {
        private int _active;
        private int _maximumActive;

        public TaskCompletionSource FourStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumActive => Volatile.Read(ref _maximumActive);

        public override async Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            SetMaximum(active);
            if (active == 4)
            {
                FourStarted.TrySetResult();
            }

            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return [];
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void SetMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumActive);
                if (value <= current ||
                    Interlocked.CompareExchange(ref _maximumActive, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class DiagnosticsSink : IDiagnosticsEventSink
    {
        public List<string> Values { get; } = [];

        public List<string> Messages { get; } = [];

        public List<string> Operations { get; } = [];

        public bool IsCategoryEnabled(DiagnosticCategory category) => true;

        public string? BeginSession(
            DiagnosticCategory category,
            string operation,
            IReadOnlyList<DiagnosticField>? context = null,
            IReadOnlyCollection<string>? relatedSessionIds = null)
        {
            Operations.Add(operation);
            Add(context);
            return "session";
        }

        public void Publish(
            string? sessionId,
            string stage,
            DiagnosticStatus status,
            DiagnosticSeverity severity,
            DiagnosticSection section,
            string message,
            IReadOnlyList<DiagnosticField>? fields = null)
        {
            Operations.Add(stage);
            Messages.Add(message);
            Add(fields);
        }

        public void Relate(string? sessionId, params string?[] relatedSessionIds)
        {
        }

        public void Complete(
            string? sessionId,
            DiagnosticStatus status,
            TimeSpan elapsed,
            string message,
            DiagnosticSeverity severity = DiagnosticSeverity.Information,
            IReadOnlyList<DiagnosticField>? fields = null)
        {
            Messages.Add(message);
            Add(fields);
        }

        private void Add(IReadOnlyList<DiagnosticField>? fields)
        {
            if (fields is null)
            {
                return;
            }

            foreach (var field in fields)
            {
                Values.Add(field.Name);
                Values.Add(field.Value);
            }
        }
    }

    private sealed class RecordingAssistant : IAiSearchAssistant
    {
        public IReadOnlyList<RankedSearchCandidate> Candidates { get; private set; } = [];

        public Task<AiSearchRerankResult> RerankAsync(
            SearchInterpretation interpretation,
            IReadOnlyList<RankedSearchCandidate> candidates,
            AiSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Candidates = candidates;
            return Task.FromResult(new AiSearchRerankResult(
                candidates.Reverse().ToArray(),
                new AiSearchAssistanceResult(
                    AiSearchAssistanceState.Applied,
                    "Synthetic known-result order applied.",
                    candidates.Count,
                    true)));
        }
    }
}
