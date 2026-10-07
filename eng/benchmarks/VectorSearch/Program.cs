using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OpenSorSe.AI;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Indexing.Sqlite;

// Opt-in only: synthetic sources and a unique temporary database; never opens the user's profile.
var output = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "vector-search-benchmark.json");
var modelName = args.ElementAtOrDefault(1) ?? "qwen3-embedding:4b";
var root = Path.Combine(Path.GetTempPath(), "OmniSorSe-vector-benchmark", Guid.NewGuid().ToString("N"));
var sourceRoot = Path.Combine(root, "sources");
Directory.CreateDirectory(sourceRoot);
var database = Path.Combine(root, "deep-index.db");
var corpus = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["trail-notes.txt"] = "Camping equipment for overnight stays in the wilderness. Pitch a waterproof tent, unroll a sleeping bag and carry a warm ground mat. Pack a headlamp for the campsite and keep food safe from animals.",
    ["monthly-ledger.txt"] = "Family budget for recurring household expenditure. Track rent, groceries, electricity, insurance and mortgage payments. Compare monthly income with expenses to calculate savings and avoid overspending.",
    ["garage-notes.txt"] = "Automobile maintenance instructions. Replace worn brake pads, check the engine oil and inflate the tires. A mechanic can diagnose unusual motor noise and repair the vehicle before a long drive.",
    ["invoice-2026-104.txt"] = "Invoice 104 for office stationery. Supplier Acme Paper, purchase order 562. Total amount 128 euros, payment due thirty days after delivery. Keep this receipt for business accounting.",
    ["baking-notes.txt"] = "Sourdough bread recipe. Feed the starter, mix flour and water, knead the dough, and leave it to ferment. Bake the loaf in a hot oven until the crust is golden brown.",
    ["conference-notes.txt"] = "Software engineering conference schedule. Speakers discuss dependency injection, distributed databases and unit testing. Workshops cover compiler architecture and code review.",
    ["garden-notes.txt"] = "Growing tomatoes in a vegetable garden. Plant seeds in fertile soil, water regularly, prune side shoots and protect leaves from frost. Harvest ripe fruit during summer.",
    ["music-notes.txt"] = "Piano practice includes major scales, chord progressions, rhythm exercises and sight reading. Use a metronome to develop consistent tempo while learning a classical sonata.",
    ["travel-notes.txt"] = "Rail itinerary through northern Italy. Book train tickets to Venice and Florence, reserve hotel rooms near the station, and visit art museums between journeys.",
    ["fitness-notes.txt"] = "Strength training program with squats, deadlifts and pushups. Warm up before lifting weights, rest between sets and allow muscles time to recover.",
    ["astronomy-notes.txt"] = "A telescope observing guide to Jupiter and Saturn. Plan a night under dark skies and use star charts to locate planets, constellations and distant galaxies.",
    ["language-notes.txt"] = "German language study plan. Learn noun genders, verb conjugation and sentence order. Practice conversation and listening comprehension with short daily exercises.",
};
var cases = new[]
{
    new QueryCase("invoice-2026-104.txt", "invoice-2026-104.txt", "exact"),
    new QueryCase("sourdough bread", "baking-notes.txt", "keyword"),
    new QueryCase("sleep outside away from home", "trail-notes.txt", "paraphrase"),
    new QueryCase("manage the money our family spends", "monthly-ledger.txt", "paraphrase"),
    new QueryCase("fix problems with my car", "garage-notes.txt", "paraphrase"),
    new QueryCase("learn to play a keyboard instrument", "music-notes.txt", "paraphrase"),
    new QueryCase("raise edible plants in the backyard", "garden-notes.txt", "paraphrase"),
    new QueryCase("quantum chromodynamics renormalization", null, "out-of-corpus"),
};
var observations = new List<IndexingFileObservation>();
var originals = new Dictionary<string, SourceSnapshot>(StringComparer.Ordinal);
foreach (var (name, content) in corpus)
{
    var path = Path.Combine(sourceRoot, name);
    await File.WriteAllTextAsync(path, content);
    var info = new FileInfo(path);
    var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
    originals[name] = new(hash, info.CreationTimeUtc, info.LastWriteTimeUtc);
    observations.Add(new(path, name, name, "synthetic-volume", info.Length,
        info.CreationTimeUtc, info.LastWriteTimeUtc, FileAttributes.Normal, hash));
}

var configuration = new BenchConfiguration(modelName);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
var provider = new CountingProvider(new OllamaEmbeddingProvider(http, configuration));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var token = timeout.Token;
var total = Stopwatch.StartNew();
var rows = new List<QueryResult>();
object report;
var passed = false;
try
{
    var identity = await provider.GetModelAsync(token) ?? throw new InvalidOperationException("Selected installed embedding model is unavailable; no model is downloaded.");
    var indexing = Stopwatch.StartNew();
    await using (var store = new SqliteDeepIndexStore(database, PlatformServices.CurrentPathSemantics))
    {
        await store.InitializeAsync(token);
        await store.UpsertSourceAsync(new("benchmark", sourceRoot, "Synthetic benchmark", IndexingLevel.Standard, true, true, 0, []), token);
        await PopulateAsync(store, observations, corpus, token);
        await using var coordinator = new VectorIndexCoordinator(configuration, store, provider);
        await coordinator.RefreshAsync(cancellationToken: token);
        if (coordinator.CurrentStatus.State != VectorIndexState.Idle || coordinator.CurrentStatus.PendingFiles != 0)
            throw new InvalidOperationException($"Vector indexing incomplete: {coordinator.CurrentStatus.Message}");
    }
    indexing.Stop();

    // Reopen the durable catalog, then ensure a refresh makes no redundant embedding requests.
    await using var reopened = new SqliteDeepIndexStore(database, PlatformServices.CurrentPathSemantics);
    await reopened.InitializeAsync(token);
    var beforeRestart = provider.EmbedCalls;
    await using var refresh = new VectorIndexCoordinator(configuration, reopened, provider);
    await refresh.RefreshAsync(cancellationToken: token);
    var restartReusedVectors = provider.EmbedCalls == beforeRestart;
    var search = new SemanticSearchService(configuration, new FeatureHashingEmbeddingProvider(), new EmptyStore(),
        new CatalogSource(reopened), modelEmbeddingProvider: provider, vectorSearchStore: reopened);
    foreach (var query in cases)
    {
        configuration.SetEmbeddings(false);
        var keyword = await search.SearchAsync(new SearchRequest(query.Text), token);
        configuration.SetEmbeddings(true);
        var watch = Stopwatch.StartNew();
        var hybrid = await search.SearchAsync(new SearchRequest(query.Text), token);
        watch.Stop();
        var names = hybrid.Hits.Select(hit => hit.FileName).ToArray();
        var rank = query.Expected is null ? 0 : Array.IndexOf(names, query.Expected) + 1;
        rows.Add(new(query.Text, query.Kind, query.Expected, keyword.Hits.Take(5).Select(hit => hit.FileName).ToArray(),
            names.Take(5).ToArray(), rank, watch.Elapsed.TotalMilliseconds,
            hybrid.Hits.Take(5).Select(hit => hit.Explanation).ToArray()));
    }

    configuration.SetEmbeddings(true, "omnisorse-benchmark-model-that-is-not-installed");
    var fallback = await search.SearchAsync(new SearchRequest("invoice-2026-104.txt"), token);
    var fallbackWorks = fallback.Hits.FirstOrDefault()?.FileName == "invoice-2026-104.txt" &&
        fallback.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    configuration.SetEmbeddings(true, modelName);

    var documents = await reopened.GetSearchDocumentsAsync(100, token);
    var seed = documents.Single(document => document.FileName == "trail-notes.txt");
    var related = await new SemanticRelatedFilesService(configuration, provider, reopened, new CatalogSource(reopened))
        .GetRelatedAsync(seed.FileId, token);
    var relatedIsSuggestion = related.Files.Count > 0 &&
        related.Files.All(file => file.Explanation.Contains("Semantic similarity only", StringComparison.Ordinal));

    // A new catalog entry is the only new work; original source files and existing vectors stay untouched.
    const string addedName = "new-field-notes.txt";
    const string addedText = "A freshwater aquarium requires a filter, clean water and suitable food for the fish.";
    var addedPath = Path.Combine(sourceRoot, addedName);
    await File.WriteAllTextAsync(addedPath, addedText, token);
    var addedInfo = new FileInfo(addedPath);
    var added = new IndexingFileObservation(addedPath, addedName, addedName, "synthetic-volume", addedInfo.Length,
        addedInfo.CreationTimeUtc, addedInfo.LastWriteTimeUtc, FileAttributes.Normal, "new-file");
    var beforeIncremental = provider.EmbedCalls;
    await PopulateAsync(reopened, observations.Append(added).ToArray(),
        new Dictionary<string, string>(corpus, StringComparer.Ordinal) { [addedName] = addedText }, token);
    await refresh.RefreshAsync(cancellationToken: token);
    var incrementalCalls = provider.EmbedCalls - beforeIncremental;
    var incrementalWorks = incrementalCalls == 1 && refresh.CurrentStatus.IndexedFiles == corpus.Count + 1 && refresh.CurrentStatus.PendingFiles == 0;
    var sourceIntegrity = true;
    foreach (var (name, original) in originals)
    {
        var path = Path.Combine(sourceRoot, name);
        var info = new FileInfo(path);
        sourceIntegrity &= original.Hash == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, token))) &&
            original.Created == info.CreationTimeUtc && original.Modified == info.LastWriteTimeUtc;
    }

    var judged = rows.Where(row => row.Expected is not null).ToArray();
    var recallAt5 = judged.Average(row => row.Rank is > 0 and <= 5 ? 1d : 0d);
    var mrr = judged.Average(row => row.Rank > 0 ? 1d / row.Rank : 0d);
    var paraphraseGains = judged.Count(row => row.Kind == "paraphrase" && row.Rank is > 0 and <= 5 && !row.KeywordTop5.Contains(row.Expected!));
    var exactTop1 = judged.Single(row => row.Kind == "exact").Rank == 1;
    passed = recallAt5 >= 0.9 && mrr >= 0.7 && exactTop1 && paraphraseGains > 0 && sourceIntegrity && restartReusedVectors && incrementalWorks && fallbackWorks && relatedIsSuggestion;
    report = new
    {
        Passed = passed, CorpusVersion = "synthetic-v1", CapturedAtUtc = DateTimeOffset.UtcNow,
        SourceRevision = typeof(SemanticSearchService).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "SourceRevision")?.Value,
        Model = identity, CorpusFiles = corpus.Count, IndexingMilliseconds = indexing.Elapsed.TotalMilliseconds,
        RecallAt5 = recallAt5, MeanReciprocalRank = mrr, ParaphraseGains = paraphraseGains, ExactTop1 = exactTop1,
        QualityCriteria = "Recall@5 >= 0.9; MRR >= 0.7; exact top1; at least one paraphrase gain over keyword top5; all integrity/recovery/fallback gates true.",
        LatencyScope = "One measured real-model query per case after indexing; includes local HTTP, embedding, SQL and fusion. Small synthetic corpus only; no universal latency claim.",
        LatencyMedianMilliseconds = rows.OrderBy(row => row.Milliseconds).ElementAt(rows.Count / 2).Milliseconds,
        SourceIntegrity = sourceIntegrity, RestartReusedVectors = restartReusedVectors, IncrementalWorks = incrementalWorks,
        IncrementalEmbeddingCalls = incrementalCalls, UnavailableFallbackWorks = fallbackWorks, RelatedIsSuggestion = relatedIsSuggestion,
        OutOfCorpusPolicy = "Unjudged diagnostic: semantic suggestions may still appear. Inspect explanations and scores; these are not verified relationships. Positive-query distractors are included in Recall/MRR.",
        Queries = rows, TotalMilliseconds = total.Elapsed.TotalMilliseconds, TemporaryWorkspace = root,
    };
}
catch (Exception exception)
{
    report = new { Passed = false, Failure = exception.Message, ModelRequested = modelName, Queries = rows, TemporaryWorkspace = root };
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Vector Search benchmark {(passed ? "passed" : "failed")}: {output}");
Console.WriteLine($"Synthetic evidence retained at {root}");
return passed ? 0 : 1;

static async Task PopulateAsync(SqliteDeepIndexStore store, IReadOnlyList<IndexingFileObservation> observations,
    IReadOnlyDictionary<string, string> content, CancellationToken token)
{
    var run = await store.BeginRunAsync("benchmark", DateTimeOffset.UtcNow, token);
    await store.EnqueueDiscoveredFilesAsync(run, observations, "vector-benchmark-v1", 1, token);
    await store.CompleteDiscoveryAsync(run, observations.Select(item => item.RelativePath).ToHashSet(StringComparer.Ordinal), DateTimeOffset.UtcNow, token);
    while (await store.ClaimNextAsync(DateTimeOffset.UtcNow, cancellationToken: token) is { } work)
    {
        var text = content[work.RelativePath];
        var next = work.Stage switch
        {
            IndexingStage.FileDiscovered => IndexingStage.MetadataIndexed,
            IndexingStage.MetadataIndexed => IndexingStage.ContentFingerprinted,
            IndexingStage.ContentFingerprinted => IndexingStage.TextExtracted,
            IndexingStage.TextExtracted => IndexingStage.SearchIndexUpdated,
            IndexingStage.SearchIndexUpdated => IndexingStage.RelationshipAnalysisCompleted,
            IndexingStage.RelationshipAnalysisCompleted => IndexingStage.FileFullyIndexed,
            IndexingStage.FileFullyIndexed => (IndexingStage?)null,
            _ => throw new InvalidOperationException($"Unexpected benchmark stage {work.Stage}."),
        };
        await store.SaveStageOutputAsync(work, new IndexingStageOutput
        {
            Status = IndexingStageStatus.Complete,
            ContentHash = work.Stage == IndexingStage.ContentFingerprinted ? Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))) : null,
            ExtractedText = work.Stage == IndexingStage.TextExtracted ? text : null,
        }, next, DateTimeOffset.UtcNow, TimeSpan.Zero, retryAtUtc: null, cancellationToken: token);
    }
}

internal sealed record QueryCase(string Text, string? Expected, string Kind);
internal sealed record QueryResult(string Query, string Kind, string? Expected, string[] KeywordTop5, string[] HybridTop5,
    int Rank, double Milliseconds, string[] Explanations);
internal sealed record SourceSnapshot(string Hash, DateTime Created, DateTime Modified);
internal sealed class BenchConfiguration(string model) : IConfigurationService
{
    public ApplicationSettings Current { get; private set; } = Make(true, model);
    public void SetEmbeddings(bool enabled, string? selected = null) => Current = Make(enabled, selected ?? Current.SemanticSearch.EmbeddingModel);
    private static ApplicationSettings Make(bool enabled, string model) => new()
    {
        SemanticSearch = new() { Enabled = true, EmbeddingsEnabled = enabled, EmbeddingModel = model, MaximumResultCount = 20 },
        Ai = new() { Endpoint = "http://127.0.0.1:11434", RequestTimeoutSeconds = 120 },
    };
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => throw new NotSupportedException();
}
internal sealed class CountingProvider(IModelEmbeddingProvider inner) : IModelEmbeddingProvider
{
    public int EmbedCalls { get; private set; }
    public Task<SemanticModelIdentity?> GetModelAsync(CancellationToken cancellationToken = default) => inner.GetModelAsync(cancellationToken);
    public Task<ModelEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, SemanticModelIdentity? expectedModel = null, CancellationToken cancellationToken = default)
    {
        EmbedCalls++;
        return inner.EmbedAsync(inputs, expectedModel, cancellationToken);
    }
}
internal sealed class EmptyStore : ISemanticIndexStore
{
    public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SemanticIndexEntry>>([]);
    public Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ClearAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
internal sealed class CatalogSource(IDeepIndexStore store) : IProgressiveSearchSource, IProgressiveSearchDocumentLookup, IProgressiveDiscoverySearchSource
{
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(int maximumCount, CancellationToken cancellationToken = default) => store.GetSearchDocumentsAsync(maximumCount, cancellationToken);
    public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(IReadOnlyList<string> fileIds, CancellationToken cancellationToken = default) => store.GetSearchDocumentsByIdsAsync(fileIds, cancellationToken);
    public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) => store.GetSearchCoverageAsync(cancellationToken);
    public Task<IReadOnlyList<string>> GetExcludedPathsAsync(int maximumCount, CancellationToken cancellationToken = default) => store.GetExcludedSearchPathsAsync(maximumCount, cancellationToken);
    public async Task<ProgressiveDiscoveryResult> GetDiscoveryCandidatesAsync(DiscoverySearchRequest request, CancellationToken cancellationToken = default)
    {
        var selected = await store.SelectSearchCandidateIdsAsync(request, cancellationToken);
        var documents = new List<ProgressiveSearchDocument>();
        foreach (var batch in selected.FileIds.Chunk(RelationshipLimits.MaximumSearchExpansions))
            documents.AddRange(await store.GetSearchDocumentsByIdsAsync(batch, cancellationToken));
        return new(documents, new(selected.EligibleFileCount, selected.MatchingFileCount, documents.Count, selected.WasTruncated, true));
    }
}
