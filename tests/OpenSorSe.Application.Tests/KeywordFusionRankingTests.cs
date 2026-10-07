using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Application.Tests;

/// <summary>Prevents deterministic hash similarity from masquerading as a second lexical vote in learned fusion.</summary>
public sealed class KeywordFusionRankingTests
{
    /// <summary>Function-word-only distractors cannot outvote a strong semantic paraphrase through a second ranking channel.</summary>
    [Fact]
    public void LearnedFusion_FunctionWordDistractorsDoNotReceiveKeywordVotes()
    {
        var ranker = new HybridSearchRanker(new UnexpectedEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new DeterministicSearchQueryInterpreter().Interpret(new SearchRequest("repair faults with my automobile"));
        var relevant = Document("garage", "Engine servicing and vehicle diagnostics.");
        var distractors = new[]
        {
            Document("astronomy", "Observe distant galaxies."),
            Document("language", "Practice conversation with a tutor."),
            Document("budget", "Review my household expenses."),
            Document("fitness", "Exercise with a training partner."),
            Document("garden", "Plant my vegetable seedlings."),
            Document("music", "Perform with a chamber orchestra."),
        };
        var documents = new[] { relevant }.Concat(distractors).ToArray();
        var matches = documents.Select((document, index) => new VectorSearchMatch(
            document.FileId!, "chunk-" + index, 0.95 - index * 0.05, "native.text", 0,
            document.ExtractedText!.Length, document.ExtractedText)).ToArray();

        var keywords = ranker.RankKeywords(interpretation, documents, 10, default);
        var fused = ReciprocalRankFusion.Fuse(keywords, documents, matches, new("test", "learned", "digest", 2), 5);

        Assert.Empty(keywords);
        Assert.Equal("garage", fused[0].Document.FileId);
        Assert.All(fused, hit => Assert.Contains(hit.Components,
            component => component.Explanation.Contains("keyword rank absent", StringComparison.Ordinal)));
    }

    /// <summary>Useful literal terms still contribute while the original phrase and exact filename intent remain intact.</summary>
    [Fact]
    public void KeywordRanking_RetainsTopicTermsExactFilenamesAndFullPhrases()
    {
        var ranker = new HybridSearchRanker(new UnexpectedEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new DeterministicSearchQueryInterpreter().Interpret(new SearchRequest("my automobile"));
        var documents = new[]
        {
            Document("exact", "Unrelated text") with { FileName = "my automobile.txt" },
            Document("phrase", "Notes about my automobile ownership."),
            Document("topic", "An automobile service schedule."),
            Document("astronomy", "Star charts with my observations."),
        };

        var result = ranker.RankKeywords(interpretation, documents, 10, default);

        Assert.Equal("exact", result[0].Document.FileId);
        Assert.Contains(result[0].Components, component => component.Kind == SearchRankingSignalKind.ExactFilenameStem);
        Assert.Contains(result.Single(hit => hit.Document.FileId == "phrase").Components,
            component => component.Kind == SearchRankingSignalKind.ExactPhrase);
        Assert.Contains(result, hit => hit.Document.FileId == "topic");
        Assert.DoesNotContain(result, hit => hit.Document.FileId == "astronomy");
    }

    /// <summary>Function words remain searchable when they are the whole query, and compatibility ranking keeps its existing behavior.</summary>
    [Fact]
    public void KeywordRanking_AllFunctionWordsAndDisabledFallbackRemainSearchable()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpreter = new DeterministicSearchQueryInterpreter();
        var candidate = Document("astronomy", "Star charts with my observations.");

        Assert.Single(ranker.RankKeywords(interpreter.Interpret(new SearchRequest("with my")), [candidate], 10, default));
        var substantive = interpreter.Interpret(new SearchRequest("repair faults with my automobile"));
        Assert.Single(ranker.Rank(substantive, [candidate], 10, default));
        Assert.Empty(ranker.RankKeywords(substantive, [candidate], 10, default));
    }

    private static SearchCandidateDocument Document(string id, string text) => new()
    {
        FileId = id,
        FullPath = id + ".txt",
        FileName = id + ".txt",
        ExtractedText = text,
    };

    /// <summary>A hash-only match retains compatibility fallback but contributes only one learned vote to RRF.</summary>
    [Fact]
    public void LearnedFusion_HashOnlyCandidate_HasNoKeywordVote()
    {
        var embeddings = new FeatureHashingEmbeddingProvider();
        var ranker = new HybridSearchRanker(embeddings, new SearchSnippetFactory());
        var interpretation = new DeterministicSearchQueryInterpreter().Interpret(new SearchRequest("outdoor sleeping"));
        var candidate = new SearchCandidateDocument
        {
            FileId = "camping",
            FullPath = "qzv.txt",
            FileName = "qzv.txt",
            SemanticRepresentation = embeddings.Embed("outdoor sleeping"),
        };

        var fallback = ranker.Rank(interpretation, [candidate], 10, default);
        var keyword = ranker.RankKeywords(interpretation, [candidate], 10, default);
        var fused = ReciprocalRankFusion.Fuse(keyword, [candidate],
            [new("camping", "chunk-1", 0.9, "native text", 0, 6, "A tent")],
            new("test", "learned-model", "digest", 2), 10);

        Assert.Single(fallback);
        Assert.Empty(keyword);
        var hit = Assert.Single(fused);
        Assert.Equal(1d / 61, hit.Score, 12);
        Assert.Single(hit.Components, component => component.Kind == SearchRankingSignalKind.SemanticSimilarity);
        Assert.Contains(hit.Components, component => component.Explanation.Contains("keyword rank absent", StringComparison.Ordinal));
    }

    /// <summary>Lexical fusion still honors the legacy semantic-availability filter without evaluating hash similarity.</summary>
    [Fact]
    public void KeywordRanking_PreservesAvailabilityFilterAndLiteralEvidence_WithoutHashing()
    {
        var ranker = new HybridSearchRanker(new UnexpectedEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new DeterministicSearchQueryInterpreter().Interpret(new SearchRequest("semantic available invoice"));
        var candidate = new SearchCandidateDocument
        {
            FileId = "invoice",
            FullPath = "invoice.txt",
            FileName = "invoice.txt",
            SemanticRepresentation = [1, 0],
        };

        var result = ranker.RankKeywords(interpretation, [candidate], 10, default);

        var hit = Assert.Single(result);
        Assert.Contains(hit.Components, component => component.Kind == SearchRankingSignalKind.Filter);
        Assert.Contains(hit.Components, component => component.Kind == SearchRankingSignalKind.ExactFilenameStem);
        Assert.DoesNotContain(hit.Components, component => component.Kind == SearchRankingSignalKind.SemanticSimilarity);
        Assert.Empty(ranker.RankKeywords(interpretation, [candidate with { SemanticRepresentation = [] }], 10, default));
    }

    private sealed class UnexpectedEmbeddingProvider : IEmbeddingProvider
    {
        public int Dimensions => 2;
        public IReadOnlyList<float> Embed(string text) => throw new InvalidOperationException("Keyword-only ranking must not embed the query.");
    }
}
