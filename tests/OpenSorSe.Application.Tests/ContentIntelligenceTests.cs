using System.Diagnostics;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Validates bounded deterministic Content Intelligence and its retrieval signals.</summary>
public sealed class ContentIntelligenceTests
{
    /// <summary>Topics, entities, and summaries remain bounded, normalized, and source-grounded.</summary>
    [Fact]
    public async Task DeterministicProviderProducesBoundedGroundedEvidence()
    {
        var provider = new DeterministicContentIntelligenceProvider();
        var settings = new ContentIntelligenceSettings
        {
            MaximumInputCharacters = 1_024,
            MaximumTopics = 5,
            MaximumEntities = 4,
            MaximumKeywords = 6,
            MaximumSummaryCharacters = 128,
            MaximumEvidenceExcerptCharacters = 64,
        };

        var result = await provider.AnalyzeAsync(
            new ContentIntelligenceRequest(
                [new ContentIntelligenceSourceText(
                    ContentEvidenceSourceKind.ExtractedText,
                    "Docker Compose deploys Prometheus and Grafana on a Raspberry Pi. The monitoring dashboard is maintained by OpenSorSe GmbH in Stuttgart.")]),
            settings,
            CancellationToken.None);

        var intelligence = Assert.IsType<IndexedContentIntelligence>(result.Intelligence);
        Assert.InRange(intelligence.Topics.Count, 1, settings.MaximumTopics);
        Assert.InRange(intelligence.Entities.Count, 1, settings.MaximumEntities);
        Assert.InRange(intelligence.Keywords.Count, 1, settings.MaximumKeywords);
        Assert.NotNull(intelligence.Summary);
        Assert.True(intelligence.Summary!.Text.Length <= settings.MaximumSummaryCharacters);
        Assert.All(
            intelligence.Topics.Concat(intelligence.Entities).SelectMany(item => item.Evidence),
            evidence => Assert.InRange(evidence.Excerpt.Length, 1, settings.MaximumEvidenceExcerptCharacters));
        Assert.Equal(ContentIntelligenceOrigin.Deterministic, intelligence.Topics[0].Origin);
    }

    /// <summary>Capitalized entity phrases stop at sentence punctuation rather than consuming the next sentence.</summary>
    [Fact]
    public async Task TextualEntitiesDoNotCrossSentenceBoundaries()
    {
        var result = await new DeterministicContentIntelligenceProvider().AnalyzeAsync(
            new ContentIntelligenceRequest(
                [new(ContentEvidenceSourceKind.ExtractedText, "Monitoring runs on Raspberry Pi. OpenSorSe maintains Grafana in Stuttgart.")]),
            new ContentIntelligenceSettings { MaximumEntities = 16 },
            CancellationToken.None);

        var intelligence = Assert.IsType<IndexedContentIntelligence>(result.Intelligence);
        Assert.Contains(intelligence.Entities, item => item.DisplayName == "Raspberry Pi");
        Assert.Contains(intelligence.Entities, item => item.DisplayName == "OpenSorSe");
        Assert.DoesNotContain(intelligence.Entities, item => item.DisplayName.Contains(". ", StringComparison.Ordinal));
    }

    /// <summary>Disabled intelligence performs no derivation and remains an ordinary optional capability.</summary>
    [Fact]
    public async Task DisabledProviderReturnsNoDerivedEvidence()
    {
        var result = await new DeterministicContentIntelligenceProvider().AnalyzeAsync(
            new ContentIntelligenceRequest([new(ContentEvidenceSourceKind.ExtractedText, "Raspberry Pi")]),
            new ContentIntelligenceSettings { Enabled = false },
            CancellationToken.None);

        Assert.True(result.WasSkipped);
        Assert.Null(result.Intelligence);
    }

    /// <summary>Oversized and malformed Unicode evidence is consumed only within the configured deterministic bound.</summary>
    [Fact]
    public async Task ProviderBoundsOversizedMalformedEvidenceBeforeExtraction()
    {
        var provider = new DeterministicContentIntelligenceProvider();
        var oversized = new string('x', 2_000_000) + "\uD800 Raspberry Pi";
        var settings = new ContentIntelligenceSettings
        {
            MaximumInputCharacters = 1_024,
            MaximumTopics = 4,
            MaximumEntities = 4,
            MaximumKeywords = 4,
        };

        var first = await provider.AnalyzeAsync(
            new ContentIntelligenceRequest([new(ContentEvidenceSourceKind.ExtractedText, oversized)]),
            settings,
            CancellationToken.None);
        var second = await provider.AnalyzeAsync(
            new ContentIntelligenceRequest([new(ContentEvidenceSourceKind.ExtractedText, oversized)]),
            settings,
            CancellationToken.None);

        var firstIntelligence = Assert.IsType<IndexedContentIntelligence>(first.Intelligence);
        var secondIntelligence = Assert.IsType<IndexedContentIntelligence>(second.Intelligence);
        Assert.Equal(firstIntelligence.ProcessingFingerprint, secondIntelligence.ProcessingFingerprint);
        Assert.Equal(
            firstIntelligence.Topics.Select(topic => topic.NormalizedValue),
            secondIntelligence.Topics.Select(topic => topic.NormalizedValue));
        Assert.InRange(firstIntelligence.Topics.Count, 1, settings.MaximumTopics);
        Assert.All(firstIntelligence.Topics, topic => Assert.True(topic.NormalizedValue.Length <= 96));
    }

    /// <summary>Each derived category can be disabled without requiring or contacting an AI provider.</summary>
    [Fact]
    public async Task IndividualCategoryControlsAreHonored()
    {
        var result = await new DeterministicContentIntelligenceProvider().AnalyzeAsync(
            new ContentIntelligenceRequest(
                [new(ContentEvidenceSourceKind.ExtractedText, "Docker Compose runs Grafana on Raspberry Pi in Stuttgart.")]),
            new ContentIntelligenceSettings
            {
                TopicExtractionEnabled = false,
                EntityExtractionEnabled = false,
                SummaryGenerationEnabled = false,
            },
            CancellationToken.None);

        var intelligence = Assert.IsType<IndexedContentIntelligence>(result.Intelligence);
        Assert.Empty(intelligence.Topics);
        Assert.Empty(intelligence.Entities);
        Assert.Empty(intelligence.Keywords);
        Assert.Null(intelligence.Summary);
    }

    /// <summary>Cancellation is observed before deterministic extraction publishes a partial result.</summary>
    [Fact]
    public async Task CancelledAnalysisDoesNotPublishPartialIntelligence()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DeterministicContentIntelligenceProvider().AnalyzeAsync(
                new ContentIntelligenceRequest(
                    [new(ContentEvidenceSourceKind.ExtractedText, "Raspberry Pi monitoring")]),
                new ContentIntelligenceSettings(),
                cancellation.Token));
    }

    /// <summary>Guards bounded topic/entity/summary extraction against catastrophic CPU or allocation regression.</summary>
    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task BoundedContentExtractionRemainsPractical()
    {
        var provider = new DeterministicContentIntelligenceProvider();
        var settings = new ContentIntelligenceSettings();
        var text = string.Join(
            ' ',
            Enumerable.Repeat(
                "Docker Compose deploys Prometheus Grafana monitoring on Raspberry Pi for OpenSorSe GmbH in Stuttgart.",
                1_000));
        var request = new ContentIntelligenceRequest(
            [new(ContentEvidenceSourceKind.ExtractedText, text)]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 50; index++)
        {
            var result = await provider.AnalyzeAsync(request, settings, CancellationToken.None);
            Assert.InRange(Assert.IsType<IndexedContentIntelligence>(result.Intelligence).Topics.Count, 1, settings.MaximumTopics);
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Content extraction took {stopwatch.Elapsed}.");
        Assert.True(allocated < 220_000_000, $"Content extraction allocated {allocated:N0} bytes.");
    }

    /// <summary>Topics and textual entities are explicit ranking components below exact filename tiers.</summary>
    [Fact]
    public void ContentSignalsImproveRankingWithoutOvertakingExactFilename()
    {
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());
        var interpretation = new SearchInterpretation(
            "Raspberry Pi",
            "Raspberry Pi",
            ["raspberry", "pi"],
            []);
        var exact = Candidate("Raspberry Pi.txt", null);
        var derived = Candidate(
            "meeting-audio.m4a",
            Intelligence(
                [Concept(ContentConceptKind.Topic, "Raspberry Pi monitoring")],
                [Concept(ContentConceptKind.ProductOrProject, "Raspberry Pi")]));

        var results = ranker.Rank(interpretation, [derived, exact], 10, CancellationToken.None);

        Assert.Equal("Raspberry Pi.txt", results[0].Document.FileName);
        var derivedResult = Assert.Single(results, item => item.Document.FileName == "meeting-audio.m4a");
        Assert.Contains(derivedResult.Components, item => item.Kind == SearchRankingSignalKind.ContentTopic);
        Assert.Contains(derivedResult.Components, item => item.Kind == SearchRankingSignalKind.ContentEntity);
        Assert.True(derivedResult.Snippet is { Source: SearchSnippetSource.ContentTopic or SearchSnippetSource.ContentEntity });
        Assert.Contains(derivedResult.Components, item => item.Explanation.Contains("Topic match", StringComparison.Ordinal));
        Assert.Contains(derivedResult.Components, item => item.Explanation.Contains("Entity match", StringComparison.Ordinal));
    }

    /// <summary>Shared specific concepts connect different media types while one generic topic remains insufficient.</summary>
    [Fact]
    public void RelatedFilesRequireSpecificCorroboratedConcepts()
    {
        var engine = new DeterministicRelationshipEngine(new FakeTimeProvider());
        var first = RelationshipDocument(
            "a",
            "notes.pdf",
            Intelligence(
                [Concept(ContentConceptKind.Topic, "Raspberry Pi"), Concept(ContentConceptKind.Topic, "Grafana monitoring")],
                [Concept(ContentConceptKind.ProductOrProject, "Grafana")]));
        var second = RelationshipDocument(
            "b",
            "recording.m4a",
            Intelligence(
                [Concept(ContentConceptKind.Topic, "Raspberry Pi"), Concept(ContentConceptKind.Topic, "Grafana monitoring")],
                [Concept(ContentConceptKind.ProductOrProject, "Grafana")]));
        var genericOnly = RelationshipDocument(
            "c",
            "unrelated.jpg",
            Intelligence([Concept(ContentConceptKind.Topic, "document")], []));

        var proposals = engine.Discover(first, [second, genericOnly], 10, CancellationToken.None);

        var related = Assert.Single(proposals);
        Assert.Equal("b", related.Relationship.SecondFileId);
        Assert.Contains(related.Relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.ContentTopic);
        Assert.Contains(related.Relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.ContentEntity);
    }

    /// <summary>Two non-generic shared topics are sufficient bounded evidence across otherwise unrelated media.</summary>
    [Fact]
    public void RelatedFilesAcceptTwoSpecificTopicsWithoutFilenameOrEntityOverlap()
    {
        var engine = new DeterministicRelationshipEngine(new FakeTimeProvider());
        var first = RelationshipDocument(
            "document",
            "setup-notes.pdf",
            Intelligence(
                [Concept(ContentConceptKind.Topic, "Raspberry Pi"), Concept(ContentConceptKind.Topic, "Grafana monitoring")],
                [])) with
        {
            FullPath = "C:\\documents\\setup-notes.pdf",
            RelativePath = "documents\\setup-notes.pdf",
            FolderName = "documents",
        };
        var second = RelationshipDocument(
            "audio",
            "spoken-recording.m4a",
            Intelligence(
                [Concept(ContentConceptKind.Topic, "Raspberry Pi"), Concept(ContentConceptKind.Topic, "Grafana monitoring")],
                [])) with
        {
            FullPath = "C:\\recordings\\spoken-recording.m4a",
            RelativePath = "recordings\\spoken-recording.m4a",
            FolderName = "recordings",
        };

        var proposal = Assert.Single(engine.Discover(first, [second], 10, CancellationToken.None));

        Assert.Equal(RelationshipConfidence.Low, proposal.Relationship.Confidence);
        Assert.Single(proposal.Relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.ContentTopic);
        Assert.DoesNotContain(proposal.Relationship.Evidence, item => item.Kind == RelationshipEvidenceKind.Keyword);
    }

    private static SearchCandidateDocument Candidate(string fileName, IndexedContentIntelligence? intelligence) => new()
    {
        FullPath = $"C:\\synthetic\\{fileName}",
        FileName = fileName,
        RelativePath = fileName,
        ContentIntelligence = intelligence,
        IsFullyIndexed = true,
    };

    private static RelationshipFileDocument RelationshipDocument(string id, string fileName, IndexedContentIntelligence intelligence) => new()
    {
        FileId = id,
        SourceId = "source",
        SourceName = "Synthetic",
        FullPath = $"C:\\synthetic\\{fileName}",
        RelativePath = fileName,
        FileName = fileName,
        FolderName = "synthetic",
        ContentIntelligence = intelligence,
        IsFullyIndexed = true,
    };

    private static IndexedContentIntelligence Intelligence(
        IReadOnlyList<ContentConcept> topics,
        IReadOnlyList<ContentConcept> entities) => new()
        {
            Topics = topics,
            Entities = entities,
            Keywords = topics.Concat(entities).Select(item => item.NormalizedValue).ToArray(),
            Provider = "test",
            ProviderVersion = "1",
            ProcessingFingerprint = "test-fingerprint",
        };

    private static ContentConcept Concept(ContentConceptKind kind, string value) => new()
    {
        Kind = kind,
        DisplayName = value,
        NormalizedValue = SearchTextNormalizer.Normalize(value),
        Confidence = ContentIntelligenceConfidence.Moderate,
        Provider = "test",
        ProviderVersion = "1",
        Origin = ContentIntelligenceOrigin.Deterministic,
    };

    private sealed class FakeTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    }
}
