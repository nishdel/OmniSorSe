using System.Diagnostics;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Media;

namespace OpenSorSe.Application.Tests;

/// <summary>Runs bounded synthetic Search cost checks separately identifiable from functional tests.</summary>
public sealed class SearchPerformanceRegressionTests
{
    /// <summary>Verifies one incremental relationship pass remains bounded at configured candidate limits.</summary>
    [Theory]
    [Trait("Category", "PerformanceRegression")]
    [InlineData(100)]
    [InlineData(RelationshipLimits.MaximumCandidates)]
    public void RelationshipDiscoveryRemainsBounded(int candidateCount)
    {
        var engine = new DeterministicRelationshipEngine();
        var target = RelationshipDocument("target", "battery-project-2026-1234.txt");
        var candidates = Enumerable.Range(0, candidateCount)
            .Select(index => RelationshipDocument(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"battery-project-2026-{1234 + index}.txt"))
            .ToArray();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var relationships = engine.Discover(
            target,
            candidates,
            RelationshipLimits.MaximumRelationshipsPerFile,
            CancellationToken.None);

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(relationships.Count, 1, RelationshipLimits.MaximumRelationshipsPerFile);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Relationship analysis of {candidateCount:N0} bounded candidates took {stopwatch.Elapsed}.");
        Assert.True(
            allocated < Math.Max(8_000_000, candidateCount * 50_000L),
            $"{candidateCount:N0} relationship candidates allocated {allocated:N0} bytes.");
    }

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

    /// <summary>Verifies mixed document/media ranking remains bounded without decoding media at query time.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public void MixedMediaSearchRemainsBoundedAndUsesRetainedEvidenceOnly()
    {
        const int documentCount = 5_000;
        var embeddings = new FeatureHashingEmbeddingProvider();
        var ranker = new HybridSearchRanker(embeddings, new SearchSnippetFactory());
        var candidates = Enumerable.Range(0, documentCount)
            .Select(index => new SearchCandidateDocument
            {
                FileId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FullPath = $"/synthetic/media/item-{index:D6}.{(index % 3 == 0 ? "jpg" : index % 3 == 1 ? "m4a" : "mp4")}",
                FileName = $"item-{index:D6}.{(index % 3 == 0 ? "jpg" : index % 3 == 1 ? "m4a" : "mp4")}",
                RelativePath = $"media/item-{index:D6}",
                FolderName = "media",
                MediaEvidence = new IndexedMediaEvidence
                {
                    Kind = index % 3 == 0 ? MediaKind.Image : index % 3 == 1 ? MediaKind.Audio : MediaKind.Video,
                    Metadata = new MediaMetadata
                    {
                        Kind = index % 3 == 0 ? MediaKind.Image : index % 3 == 1 ? MediaKind.Audio : MediaKind.Video,
                        DeviceModel = index % 100 == 0 ? "Synthetic Camera" : "Other Device",
                        Duration = index % 3 == 0 ? null : TimeSpan.FromSeconds(60),
                    },
                    Transcript = index % 100 == 1 ? "raspberry pi monitoring discussion" : null,
                    OcrText = index % 100 == 2 ? "docker compose up" : null,
                    MetadataProvider = "synthetic",
                    MetadataProviderVersion = "1",
                    ProcessingFingerprint = "synthetic",
                    Status = MediaExtractionStatus.Completed,
                },
                IsFullyIndexed = true,
            })
            .ToArray();
        var interpretation = new SearchInterpretation("raspberry pi", "raspberry pi", ["raspberry", "pi"], []);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var results = ranker.Rank(interpretation, candidates, 50, CancellationToken.None);

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(50, results.Count);
        Assert.All(results, result => Assert.Contains(result.Components, component => component.Kind == SearchRankingSignalKind.MediaTranscript));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Mixed-media ranking took {stopwatch.Elapsed}.");
        Assert.True(allocated < 150_000_000, $"Mixed-media ranking allocated {allocated:N0} bytes.");
    }

    private static RelationshipFileDocument RelationshipDocument(string id, string fileName) => new()
    {
        FileId = id,
        SourceId = "synthetic",
        SourceName = "Synthetic source",
        FullPath = "/synthetic/projects/" + fileName,
        RelativePath = "projects/" + fileName,
        FileName = fileName,
        FolderName = "projects",
        Extension = ".txt",
        CreationTimeUtc = DateTimeOffset.UnixEpoch,
        ModifiedTimeUtc = DateTimeOffset.UnixEpoch.AddMinutes(30),
        Keywords = ["battery", "project"],
        Summary = "Synthetic battery project record",
        IsFullyIndexed = true,
    };
}
