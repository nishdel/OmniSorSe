using System.Diagnostics;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Application.Tests;

/// <summary>Runs bounded synthetic Search cost checks separately identifiable from functional tests.</summary>
public sealed class SearchPerformanceRegressionTests
{
    /// <summary>Verifies cold/warm ranking, filtering, and snippet costs remain bounded as synthetic data grows.</summary>
    [Theory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void HybridRankingRemainsBounded(int documentCount)
    {
        var embeddings = new FeatureHashingEmbeddingProvider();
        var ranker = new HybridSearchRanker(embeddings, new SearchSnippetFactory());
        var candidates = Enumerable.Range(0, documentCount)
            .Select(index => new SearchCandidateDocument
            {
                FileId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FullPath = $@"C:\synthetic\group-{index % 20}\document-{index:D8}.txt",
                FileName = $"document-{index:D8}.txt",
                RelativePath = $@"group-{index % 20}\document-{index:D8}.txt",
                FolderName = $"group-{index % 20}",
                Extension = ".txt",
                FileType = "document",
                MetadataText = index % 50 == 0
                    ? "battery research synthetic record"
                    : "unrelated deterministic distractor",
                SemanticRepresentation = embeddings.Embed(
                    index % 50 == 0
                        ? "battery research"
                        : $"unrelated concept {index}"),
                IsFullyIndexed = index % 3 != 0,
            })
            .ToArray();
        var interpretation = new DeterministicSearchQueryInterpreter()
            .Interpret(new SearchRequest("battery research"));
        var coldStopwatch = Stopwatch.StartNew();
        var coldResults = ranker.Rank(interpretation, candidates, 50, CancellationToken.None);
        coldStopwatch.Stop();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var warmStopwatch = Stopwatch.StartNew();

        var warmResults = ranker.Rank(interpretation, candidates, 50, CancellationToken.None);

        warmStopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(coldResults.Count, 1, 50);
        Assert.Equal(
            coldResults.Select(result => result.Document.FullPath),
            warmResults.Select(result => result.Document.FullPath));
        Assert.True(
            coldStopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Cold ranking of {documentCount:N0} synthetic candidates took {coldStopwatch.Elapsed}.");
        Assert.True(
            warmStopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Warm ranking of {documentCount:N0} synthetic candidates took {warmStopwatch.Elapsed}.");
        Assert.True(
            allocated < Math.Max(8_000_000, documentCount * 30_000L),
            $"{documentCount:N0} synthetic candidates allocated {allocated:N0} bytes.");
    }

    /// <summary>Verifies cancelled fuzzy work exits before producing a partial result list.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public void CancelledFuzzyRankingHasBoundedLatency()
    {
        var embeddings = new FeatureHashingEmbeddingProvider();
        var ranker = new HybridSearchRanker(embeddings, new SearchSnippetFactory());
        var candidates = Enumerable.Range(0, 5000)
            .Select(index => new SearchCandidateDocument
            {
                FileId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FullPath = $@"C:\synthetic\document-{index:D8}.txt",
                FileName = $"document-{index:D8}.txt",
                RelativePath = $"document-{index:D8}.txt",
                FolderName = "synthetic",
            })
            .ToArray();
        var interpretation = new SearchInterpretation(
            "pathological",
            "pathological",
            ["pathological"],
            []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<OperationCanceledException>(
            () => ranker.Rank(interpretation, candidates, 50, cancellation.Token));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }
}
