using System.Globalization;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies deterministic v1.8 Search interpretation, ranking, snippets, and quality metrics.</summary>
public sealed class SearchIntelligenceTests
{
    private static readonly FeatureHashingEmbeddingProvider Embeddings = new();
    private static readonly SearchSnippetFactory Snippets = new();

    /// <summary>Verifies conservative local interpretation leaves topic terms and exposes each filter.</summary>
    [Theory]
    [InlineData("PDF invoices from 2026 mentioning Mercedes", SearchFilterKind.FileType, "pdf", "invoices mentioning Mercedes")]
    [InlineData("documents tagged tax", SearchFilterKind.Tag, "tax", "")]
    [InlineData("files in the Raspberry Pi folder about monitoring", SearchFilterKind.Folder, "Raspberry Pi", "files about monitoring")]
    [InlineData("large videos modified this month", SearchFilterKind.FileType, "video", "large")]
    [InlineData("OCR documents waiting for processing", SearchFilterKind.FileType, "document", "OCR waiting for processing")]
    [InlineData("extension:pdf battery research", SearchFilterKind.Extension, "pdf", "battery research")]
    [InlineData("source:work invoices", SearchFilterKind.Source, "work", "invoices")]
    [InlineData("source:\"work archive\" invoices", SearchFilterKind.Source, "work archive", "invoices")]
    [InlineData("metadata only tax", SearchFilterKind.IndexingLevel, "basic", "tax")]
    [InlineData("partially indexed recipes", SearchFilterKind.IndexingCompletion, "partial", "recipes")]
    [InlineData("without OCR household records", SearchFilterKind.OcrAvailability, "false", "household records")]
    [InlineData("semantic available climbing plans", SearchFilterKind.SemanticAvailability, "true", "climbing plans")]
    [InlineData("failed indexing employment", SearchFilterKind.FailureState, "true", "employment")]
    public void Interpreter_RecognizesVisibleFilters(
        string query,
        SearchFilterKind kind,
        string value,
        string expectedTopic)
    {
        var result = new DeterministicSearchQueryInterpreter(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero)),
            CultureInfo.InvariantCulture)
            .Interpret(new SearchRequest(query));

        Assert.Contains(result.Filters, item => item.Kind == kind && item.Value == value);
        Assert.Equal(expectedTopic, result.TopicText, ignoreCase: true);
    }

    /// <summary>Verifies relative and absolute ranges use the injected local clock.</summary>
    [Theory]
    [InlineData("modified this month reports", "2026-07-01T00:00:00.0000000+00:00")]
    [InlineData("modified last month reports", "2026-06-01T00:00:00.0000000+00:00")]
    [InlineData("reports from 2025", "2025-01-01T00:00:00.0000000+00:00")]
    [InlineData("modified:2026-03-17 reports", "2026-03-17T00:00:00.0000000+00:00")]
    [InlineData("reports in July 2025", "2025-07-01T00:00:00.0000000+00:00")]
    public void Interpreter_DateRangesAreDeterministic(string query, string expectedStart)
    {
        var result = new DeterministicSearchQueryInterpreter(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero)),
            CultureInfo.InvariantCulture)
            .Interpret(new SearchRequest(query));

        Assert.Contains(
            result.Filters,
            item => item.Kind == SearchFilterKind.ModifiedOnOrAfter &&
                item.Value == expectedStart);
    }

    /// <summary>Verifies localized month names are interpreted with the supplied locale.</summary>
    [Fact]
    public void Interpreter_UsesSuppliedLocaleForMonthNames()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var result = new DeterministicSearchQueryInterpreter(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero)),
            culture)
            .Interpret(new SearchRequest("Berichte im Juli 2025"));

        Assert.Contains(
            result.Filters,
            item => item.Kind == SearchFilterKind.ModifiedOnOrAfter &&
                item.Value.StartsWith("2025-07-01", StringComparison.Ordinal));
    }

    /// <summary>Verifies bounded size phrases produce invariant byte filters.</summary>
    [Theory]
    [InlineData("videos larger than 2 GiB", SearchFilterKind.MinimumSizeBytes, "2147483648")]
    [InlineData("photos under 1.5 MB", SearchFilterKind.MaximumSizeBytes, "1500000")]
    public void Interpreter_SizeFiltersAreBounded(
        string query,
        SearchFilterKind kind,
        string value)
    {
        var result = new DeterministicSearchQueryInterpreter().Interpret(new SearchRequest(query));

        Assert.Contains(result.Filters, item => item.Kind == kind && item.Value == value);
    }

    /// <summary>Verifies uncertain natural language remains visible topic text.</summary>
    [Fact]
    public void Interpreter_AmbiguousPhraseRemainsTopicText()
    {
        var result = new DeterministicSearchQueryInterpreter()
            .Interpret(new SearchRequest("photos from last summer near a lake"));

        Assert.DoesNotContain(
            result.Filters,
            item => item.Kind is SearchFilterKind.ModifiedOnOrAfter or SearchFilterKind.ModifiedBefore);
        Assert.Contains("last summer", result.TopicText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies explicit user-edited filters are not silently reinterpreted.</summary>
    [Fact]
    public void Interpreter_ExplicitFiltersRoundTrip()
    {
        SearchFilter[] filters =
        [
            new("Extension:pdf", SearchFilterKind.Extension, "pdf", "Extension: pdf"),
        ];

        var result = new DeterministicSearchQueryInterpreter().Interpret(
            new SearchRequest("PDF tax files", filters, false, "tax files"));

        Assert.Equal(filters, result.Filters);
        Assert.Equal("tax files", result.TopicText);
    }

    /// <summary>Verifies hostile and pathological query inputs fail with actionable validation.</summary>
    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public void Interpreter_RejectsHostileQueries(string query)
    {
        Assert.Throws<SearchQueryValidationException>(
            () => new DeterministicSearchQueryInterpreter().Interpret(new SearchRequest(query)));
    }

    /// <summary>Supplies bounded hostile inputs without touching the developer filesystem.</summary>
    public static TheoryData<string> InvalidQueries => new()
    {
        string.Empty,
        new('a', SearchLimits.MaximumQueryCharacters + 1),
        "tax\0records",
        "broken\uD800text",
        string.Join(' ', Enumerable.Range(0, SearchLimits.MaximumQueryTokens + 1).Select(index => $"term{index}")),
    };

    /// <summary>Verifies every literal field produces its own real ranking component.</summary>
    [Theory]
    [InlineData("invoice", SearchRankingSignalKind.FilenameToken)]
    [InlineData("finance", SearchRankingSignalKind.FolderName)]
    [InlineData("work", SearchRankingSignalKind.Path)]
    [InlineData("pdf", SearchRankingSignalKind.Extension)]
    [InlineData("document", SearchRankingSignalKind.FileType)]
    [InlineData("tax", SearchRankingSignalKind.Tag)]
    [InlineData("mercedes", SearchRankingSignalKind.Metadata)]
    [InlineData("brake", SearchRankingSignalKind.ExtractedText)]
    [InlineData("odometer", SearchRankingSignalKind.OcrText)]
    [InlineData("repair", SearchRankingSignalKind.Summary)]
    [InlineData("vehicle", SearchRankingSignalKind.Keyword)]
    [InlineData("workshop", SearchRankingSignalKind.Chunk)]
    public void Ranker_EmitsActualLiteralSignal(string query, SearchRankingSignalKind expected)
    {
        var candidate = Candidate(
            "repair",
            "invoice-1042.pdf",
            @"C:\work\finance\invoice-1042.pdf") with
        {
            FolderName = "finance",
            RelativePath = @"work\finance\invoice-1042.pdf",
            Extension = ".pdf",
            FileType = "document",
            Tags = ["tax"],
            MetadataText = "Mercedes",
            ExtractedText = "brake service",
            OcrText = "odometer reading",
            Summary = "repair receipt",
            Keywords = ["vehicle"],
            Chunks = ["local workshop"],
        };

        var normalized = SearchTextNormalizer.Normalize(query);
        var result = CreateRanker().Rank(
            new SearchInterpretation(query, query, [normalized], []),
            [candidate],
            10,
            CancellationToken.None);

        Assert.Contains(Assert.Single(result).Components, item => item.Kind == expected);
    }

    /// <summary>Verifies exact filenames remain above a high related-concept-only result.</summary>
    [Fact]
    public void Ranker_ExactFilenameOutranksSemanticOnlyCandidate()
    {
        var exact = Candidate("exact", "battery.pdf", @"C:\docs\battery.pdf");
        var semantic = Candidate("semantic", "notes.bin", @"C:\docs\notes.bin") with
        {
            SemanticRepresentation = Embeddings.Embed("battery.pdf"),
        };

        var result = Rank("battery.pdf", semantic, exact);

        Assert.Equal("exact", result[0].Document.FileId);
        Assert.Equal(SearchRankingSignalKind.ExactFilename, result[0].Components[0].Kind);
    }

    /// <summary>Verifies documented filename tiers remain explicit and stronger than weaker field matches.</summary>
    [Fact]
    public void Ranker_FilenameTiersAreDeterministicAndExplainable()
    {
        var exact = Candidate("exact", "raspberry setup", @"C:\docs\raspberry setup");
        var stem = Candidate("stem", "raspberry setup.pdf", @"C:\docs\raspberry setup.pdf");
        var prefix = Candidate("prefix", "raspberry setup notes.pdf", @"C:\docs\raspberry setup notes.pdf");
        var substring = Candidate("substring", "old-raspberry_setup-notes.pdf", @"C:\docs\old-raspberry_setup-notes.pdf");
        var content = Candidate("content", "opaque.bin", @"C:\docs\opaque.bin") with
        {
            ExtractedText = "raspberry setup",
        };

        var result = Rank("raspberry setup", content, substring, prefix, stem, exact);

        Assert.Equal(["exact", "stem", "prefix", "substring", "content"],
            result.Select(item => item.Document.FileId));
        Assert.Contains(result[0].Components, item => item.Kind == SearchRankingSignalKind.ExactFilename);
        Assert.Contains(result[1].Components, item => item.Kind == SearchRankingSignalKind.ExactFilenameStem);
        Assert.Contains(result[2].Components, item => item.Kind == SearchRankingSignalKind.FilenamePrefix);
        Assert.Contains(result[3].Components, item => item.Kind == SearchRankingSignalKind.FilenameSubstring);
        Assert.Contains(result[4].Components, item => item.Kind == SearchRankingSignalKind.ExactPhrase);
    }

    /// <summary>Verifies a complete literal document phrase remains stronger than a weak partial filename token.</summary>
    [Fact]
    public void Ranker_StrongContentPhraseOutranksWeakPartialFilename()
    {
        var content = Candidate("content", "opaque.dat", @"C:\docs\opaque.dat") with
        {
            ExtractedText = "battery degradation research",
        };
        var partialName = Candidate("partial", "battery-charger.jpg", @"C:\photos\battery-charger.jpg");

        var result = Rank("battery degradation research", partialName, content);

        Assert.Equal("content", result[0].Document.FileId);
        Assert.Contains(result[0].Components, item => item.Kind == SearchRankingSignalKind.ExactPhrase);
    }

    /// <summary>Verifies an exact retained phrase is distinguished from independent token matches.</summary>
    [Fact]
    public void Ranker_ExactPhraseProducesExplicitEvidence()
    {
        var phrase = Candidate("phrase", "opaque.dat", @"C:\docs\opaque.dat") with
        {
            ExtractedText = "vehicle repair invoice",
        };

        var result = Assert.Single(Rank("repair invoice", phrase));

        Assert.Contains(
            result.Components,
            item =>
                item.Kind == SearchRankingSignalKind.ExactPhrase &&
                item.Field == "document text");
    }

    /// <summary>Verifies a related-concept-only result remains possible and explicitly explained.</summary>
    [Fact]
    public void Ranker_SemanticOnlyResultCarriesActualRelatedConceptEvidence()
    {
        var candidate = Candidate("semantic", "opaque.bin", @"C:\docs\opaque.bin") with
        {
            SemanticRepresentation = Embeddings.Embed("battery degradation research"),
        };

        var result = Assert.Single(Rank("battery degradation research", candidate));

        Assert.Contains(
            result.Components,
            item => item.Kind == SearchRankingSignalKind.SemanticSimilarity);
        Assert.DoesNotContain(
            result.Components,
            item => item.Kind is SearchRankingSignalKind.ExactFilename or
                SearchRankingSignalKind.ExactPhrase or
                SearchRankingSignalKind.FilenameToken);
    }

    /// <summary>Verifies deterministic source, completeness, recency, and path tie-breaking.</summary>
    [Fact]
    public void Ranker_TieBreakingIsStableAndExplicit()
    {
        var older = Candidate("older", "report-a.txt", @"C:\b\report-a.txt") with
        {
            ExtractedText = "battery research",
            ModifiedTimeUtc = DateTimeOffset.UnixEpoch,
        };
        var priority = Candidate("priority", "report-b.txt", @"C:\a\report-b.txt") with
        {
            ExtractedText = "battery research",
            SourcePriority = 10,
            IsFullyIndexed = true,
            ModifiedTimeUtc = DateTimeOffset.UnixEpoch.AddDays(1),
        };

        var first = Rank("battery", older, priority);
        var second = Rank("battery", older, priority);

        Assert.Equal("priority", first[0].Document.FileId);
        Assert.Equal(
            first.Select(item => item.Document.FileId),
            second.Select(item => item.Document.FileId));
        Assert.Contains(first[0].Components, item => item.Kind == SearchRankingSignalKind.SourcePriority);
    }

    /// <summary>Verifies completeness and recency resolve equal literal evidence deterministically.</summary>
    [Fact]
    public void Ranker_CompletenessAndRecencyResolveOtherwiseEqualResults()
    {
        var partial = Candidate("partial", "a.txt", @"C:\a.txt") with
        {
            ExtractedText = "battery",
            ModifiedTimeUtc = DateTimeOffset.UnixEpoch.AddDays(10),
            IsFullyIndexed = false,
        };
        var complete = Candidate("complete", "b.txt", @"C:\b.txt") with
        {
            ExtractedText = "battery",
            ModifiedTimeUtc = DateTimeOffset.UnixEpoch.AddDays(1),
            IsFullyIndexed = true,
        };

        var completeness = Rank("battery", partial, complete);
        var recency = Rank("battery", partial, complete with { IsFullyIndexed = false });

        Assert.Equal("complete", completeness[0].Document.FileId);
        Assert.Equal("partial", recency[0].Document.FileId);
        Assert.Contains(
            completeness[0].Components,
            item => item.Kind == SearchRankingSignalKind.IndexingCompleteness);
        Assert.Contains(
            recency[0].Components,
            item => item.Kind == SearchRankingSignalKind.Recency);
    }

    /// <summary>Verifies all provider-neutral filter kinds compose as logical AND.</summary>
    [Fact]
    public void Ranker_ComposesFiltersPredictably()
    {
        var candidate = Candidate("match", "tax.pdf", @"C:\work\finance\tax.pdf") with
        {
            FolderName = "finance",
            RelativePath = @"finance\tax.pdf",
            Extension = ".pdf",
            FileType = "pdf",
            SourceId = "source-1",
            SourceName = "Work",
            Length = 5000,
            CreationTimeUtc = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero),
            ModifiedTimeUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            IndexingLevel = IndexingLevel.Deep,
            Tags = ["tax"],
            OcrText = "available",
            SemanticRepresentation = Embeddings.Embed("tax"),
            HasIndexingFailure = true,
            IsFullyIndexed = true,
        };
        SearchFilter[] filters =
        [
            Filter(SearchFilterKind.FileType, "pdf"),
            Filter(SearchFilterKind.Extension, "pdf"),
            Filter(SearchFilterKind.Source, "Work"),
            Filter(SearchFilterKind.Folder, "finance"),
            Filter(SearchFilterKind.Tag, "tax"),
            Filter(SearchFilterKind.IndexingLevel, "Deep"),
            Filter(SearchFilterKind.IndexingCompletion, "full"),
            Filter(SearchFilterKind.OcrAvailability, "true"),
            Filter(SearchFilterKind.SemanticAvailability, "true"),
            Filter(SearchFilterKind.FailureState, "true"),
            Filter(SearchFilterKind.MinimumSizeBytes, "4000"),
            Filter(SearchFilterKind.MaximumSizeBytes, "6000"),
            Filter(SearchFilterKind.CreatedOnOrAfter, "2025-01-01T00:00:00.0000000+00:00"),
            Filter(SearchFilterKind.CreatedBefore, "2025-02-01T00:00:00.0000000+00:00"),
            Filter(SearchFilterKind.ModifiedOnOrAfter, "2026-01-01T00:00:00.0000000+00:00"),
            Filter(SearchFilterKind.ModifiedBefore, "2026-02-01T00:00:00.0000000+00:00"),
        ];
        var interpretation = new SearchInterpretation("tax", "tax", ["tax"], filters);

        var result = CreateRanker().Rank(interpretation, [candidate], 10, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(filters.Length, result[0].Components.Count(item => item.Kind == SearchRankingSignalKind.Filter));
    }

    /// <summary>Eligible Smart Tags explain matches without outranking an exact filename.</summary>
    [Fact]
    public void Ranker_ProtectsExactFilenameOverSmartTagEvidence()
    {
        var exact = Candidate("exact", "finance.txt", @"C:\finance.txt");
        var classified = Candidate("classified", "scan-0042.pdf", @"C:\scan-0042.pdf") with
        {
            SmartTags = [SmartTag("theme.finance", SmartTagType.Theme, "Finance", ContentIntelligenceConfidence.Strong, SmartTagAssignmentState.Automatic)],
        };

        var results = Rank("finance", classified, exact);

        Assert.Equal("exact", results[0].Document.FileId);
        Assert.Equal("classified", results[1].Document.FileId);
        Assert.Contains(results[1].Components, component =>
            component.Kind == SearchRankingSignalKind.SmartTagTheme &&
            component.Explanation == "Theme: Finance — Strong");
    }

    /// <summary>Moderate suggestions stay outside ordinary Search until explicitly accepted.</summary>
    [Fact]
    public void Ranker_ExcludesModerateSuggestionUntilAccepted()
    {
        var suggested = SmartTag(
            "theme.finance",
            SmartTagType.Theme,
            "Finance",
            ContentIntelligenceConfidence.Moderate,
            SmartTagAssignmentState.Suggested);
        var candidate = Candidate("candidate", "scan.pdf", @"C:\scan.pdf") with { SmartTags = [suggested] };

        Assert.Empty(Rank("finance", candidate));

        var accepted = candidate with
        {
            SmartTags = [suggested with { State = SmartTagAssignmentState.Accepted, Decision = SmartTagDecision.Accepted }],
        };
        var result = Assert.Single(Rank("finance", accepted));
        Assert.Contains(result.Components, component => component.Explanation == "Theme: Finance — Accepted");
    }

    /// <summary>Canonical Smart Tag filters are OR within type and AND across populated types.</summary>
    [Fact]
    public void Ranker_ComposesTypedSmartTagFiltersByApprovedSemantics()
    {
        var financeInvoice = Candidate("finance-invoice", "one.pdf", @"C:\one.pdf") with
        {
            SmartTags =
            [
                SmartTag("theme.finance", SmartTagType.Theme, "Finance"),
                SmartTag("document-type.invoice", SmartTagType.DocumentType, "Invoice"),
            ],
        };
        var legalInvoice = Candidate("legal-invoice", "two.pdf", @"C:\two.pdf") with
        {
            SmartTags =
            [
                SmartTag("theme.legal", SmartTagType.Theme, "Legal"),
                SmartTag("document-type.invoice", SmartTagType.DocumentType, "Invoice"),
            ],
        };
        var financeReport = Candidate("finance-report", "three.pdf", @"C:\three.pdf") with
        {
            SmartTags =
            [
                SmartTag("theme.finance", SmartTagType.Theme, "Finance"),
                SmartTag("document-type.report", SmartTagType.DocumentType, "Report"),
            ],
        };
        SearchFilter[] filters =
        [
            Filter(SearchFilterKind.SmartTagTheme, "theme.finance"),
            Filter(SearchFilterKind.SmartTagTheme, "theme.legal"),
            Filter(SearchFilterKind.SmartTagDocumentType, "document-type.invoice"),
        ];

        var results = CreateRanker().Rank(
            new SearchInterpretation(string.Empty, string.Empty, [], filters),
            [financeInvoice, legalInvoice, financeReport],
            10,
            CancellationToken.None);

        Assert.Equal(["finance-invoice", "legal-invoice"], results.Select(result => result.Document.FileId).OrderBy(value => value, StringComparer.Ordinal));
    }

    /// <summary>Verifies diacritics, punctuation, underscores, dashes, and separators normalize predictably.</summary>
    [Theory]
    [InlineData("München", "munchen")]
    [InlineData("tax_report-final", "tax report final")]
    [InlineData(@"folder\sub/file", "folder sub file")]
    [InlineData("résumé.pdf", "resume pdf")]
    public void Normalizer_FoldsSupportedSeparators(string input, string expected) =>
        Assert.Equal(expected, SearchTextNormalizer.Normalize(input));

    /// <summary>Verifies bounded filename typo tolerance and short-query suppression.</summary>
    [Theory]
    [InlineData("invoce", true)]
    [InlineData("invoixe", true)]
    [InlineData("invocie", true)]
    [InlineData("inx", false)]
    [InlineData("zzzzzz", false)]
    public void Ranker_TypoToleranceIsBounded(string query, bool expected)
    {
        var results = Rank(query, Candidate("invoice", "invoice.pdf", @"C:\invoice.pdf"));

        Assert.Equal(expected, results.Count > 0);
        if (expected)
        {
            Assert.Contains(results[0].Components, item => item.Kind == SearchRankingSignalKind.FuzzyFilename);
        }
    }

    /// <summary>Verifies cooperative cancellation is checked during bounded candidate selection.</summary>
    [Fact]
    public void Ranker_CancellationIsPrompt()
    {
        var candidates = Enumerable.Range(0, 5000)
            .Select(index => Candidate(index.ToString(CultureInfo.InvariantCulture), $"file-{index}.txt", $@"C:\{index}.txt"))
            .ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => Rank("unlikely-query", cancellation.Token, candidates));
    }

    /// <summary>Verifies snippets are retained-data-only, bounded, sourced, and safe for malformed text.</summary>
    [Fact]
    public void SnippetFactory_BoundsAndSanitizesText()
    {
        var candidate = Candidate("snippet", "opaque.dat", @"C:\opaque.dat") with
        {
            OcrText = new string('x', 400) + "\0 battery \uD800" + new string('y', 400),
        };
        var result = Assert.Single(Rank("battery", candidate));

        Assert.NotNull(result.Snippet);
        Assert.Equal(SearchSnippetSource.OcrText, result.Snippet.Source);
        Assert.InRange(result.Snippet.Text.Length, 1, SearchLimits.MaximumSnippetCharacters);
        Assert.DoesNotContain('\0', result.Snippet.Text);
        Assert.Contains(result.Snippet.Highlights, range => range.Length == "battery".Length);
        Assert.Equal("battery", result.Snippet.MatchedText, ignoreCase: true);
        Assert.Contains("OCR text:", result.Snippet.AccessibleText, StringComparison.Ordinal);
        Assert.Contains("battery", result.Snippet.AccessibleText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies the repeatable synthetic corpus produces stable, visible regression metrics.</summary>
    [Fact]
    [Trait("Category", "SearchRelevance")]
    public void QualityEvaluator_SyntheticCorpusMeetsRegressionFloor()
    {
        var corpus = SyntheticCorpus();
        SearchQualityCase[] cases =
        [
            Quality("Mercedes brake invoice", "vehicle"),
            Quality("tax return records", "tax"),
            Quality("holiday hotel booking", "holiday"),
            Quality("climbing route plans", "climbing"),
            Quality("battery degradation research", "battery"),
            Quality("employment contract", "employment"),
            Quality("Raspberry Pi monitoring", "raspberry"),
            Quality("pasta recipe", "recipe"),
            Quality("home insurance record", "household"),
            Quality("unrelated compiler document", "technical"),
            new("exact-record.pdf", new HashSet<string>(["exact"], StringComparer.Ordinal), "exact"),
        ];
        var evaluator = new SearchQualityEvaluator(
            new DeterministicSearchQueryInterpreter(),
            CreateRanker());

        var metrics = evaluator.Evaluate(corpus, cases, 3);
        var topResults = cases.Select(item =>
        {
            var interpreted = new DeterministicSearchQueryInterpreter().Interpret(new SearchRequest(item.Query));
            var first = CreateRanker().Rank(interpreted, corpus, corpus.Count, CancellationToken.None).FirstOrDefault();
            return $"{item.Query} [topic={interpreted.TopicText}; filters={string.Join(',', interpreted.Filters.Select(filter => filter.DisplayName))}] => {first?.Document.FileId ?? "<none>"}";
        });

        Assert.Equal(cases.Length, metrics.QueryCount);
        Assert.True(
            metrics.TopResultCorrectness is >= 0.90 and <= 1,
            $"Unexpected top results: {string.Join("; ", topResults)}");
        Assert.InRange(metrics.TopKRecall, 0.90, 1);
        Assert.InRange(metrics.MeanReciprocalRank, 0.90, 1);
        Assert.Equal(1, metrics.ExactMatchPreservation);
        Assert.True(metrics.StableOrdering);
    }

    private static IReadOnlyList<RankedSearchCandidate> Rank(
        string query,
        params SearchCandidateDocument[] candidates) =>
        Rank(query, CancellationToken.None, candidates);

    private static IReadOnlyList<RankedSearchCandidate> Rank(
        string query,
        CancellationToken cancellationToken,
        params SearchCandidateDocument[] candidates)
    {
        var interpretation = new DeterministicSearchQueryInterpreter()
            .Interpret(new SearchRequest(query));
        return CreateRanker().Rank(interpretation, candidates, 100, cancellationToken);
    }

    private static HybridSearchRanker CreateRanker() => new(Embeddings, Snippets);

    private static SearchCandidateDocument Candidate(string id, string fileName, string fullPath) => new()
    {
        FileId = id,
        FullPath = fullPath,
        FileName = fileName,
        RelativePath = fileName,
        FolderName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty,
        Extension = Path.GetExtension(fileName),
        FileType = SearchFileTypeClassifier.Classify(fileName),
        ModifiedTimeUtc = DateTimeOffset.UnixEpoch,
        IsFullyIndexed = false,
    };

    private static SearchFilter Filter(SearchFilterKind kind, string value) =>
        new($"{kind}:{value}", kind, value, $"{kind}: {value}");

    private static FileSmartTag SmartTag(
        string tagId,
        SmartTagType type,
        string display,
        ContentIntelligenceConfidence confidence = ContentIntelligenceConfidence.Strong,
        SmartTagAssignmentState state = SmartTagAssignmentState.Automatic) => new()
        {
            FileId = "test-file",
            Definition = new SmartTagDefinition
            {
                TagId = tagId,
                Type = type,
                CanonicalKey = tagId[(tagId.IndexOf('.') + 1)..],
                DisplayName = display,
                TaxonomyVersion = "1.0",
                Origin = SmartTagOrigin.BuiltInTaxonomy,
                IsBuiltIn = true,
            },
            Confidence = confidence,
            Origin = SmartTagOrigin.DeterministicClassifier,
            State = state,
            Decision = SmartTagDecision.None,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private static SearchQualityCase Quality(string query, string id) =>
        new(query, new HashSet<string>([id], StringComparer.Ordinal), id);

    private static IReadOnlyList<SearchCandidateDocument> SyntheticCorpus()
    {
        (string Id, string Name, string Folder, string Text)[] values =
        [
            ("vehicle", "document-001.pdf", "finance", "Mercedes vehicle repair brake invoice workshop"),
            ("tax", "scan-a.pdf", "records", "annual tax return declaration payment"),
            ("holiday", "confirmation.msg", "travel", "holiday hotel booking flight itinerary"),
            ("climbing", "weekend.txt", "outdoors", "climbing route plans equipment"),
            ("battery", "paper-17.pdf", "research", "battery degradation research electrode study"),
            ("employment", "signed-copy.pdf", "work", "employment contract salary"),
            ("raspberry", "notes.md", "projects", "Raspberry Pi monitoring dashboard sensors"),
            ("recipe", "card-4.txt", "kitchen", "pasta recipe tomatoes basil"),
            ("household", "policy.dat", "home", "home insurance household record"),
            ("technical", "specification.pdf", "unrelated", "unrelated compiler document bytecode"),
            ("distractor", "battery-charger.jpg", "photos", "holiday climbing photograph"),
            ("exact", "exact-record.pdf", "exact", "generic contents"),
        ];
        return values.Select(value => Candidate(
                value.Id,
                value.Name,
                $@"C:\synthetic\{value.Folder}\{value.Name}") with
        {
            FolderName = value.Folder,
            ExtractedText = value.Text,
            SemanticRepresentation = Embeddings.Embed(value.Text),
            IsFullyIndexed = true,
        })
            .ToArray();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
