using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Protects independent semantic discovery, principled fusion and optional-provider fallback.</summary>
public sealed class VectorSearchIntegrationTests
{
    private static readonly SemanticModelIdentity Model = new("test", "embedding-only", "digest-1", 2);

    /// <summary>A broad vector result preserves semantic retrieval across the production hydration batch limit.</summary>
    [Fact]
    public async Task Search_MoreThanOneHydrationBatch_DoesNotLoseSemanticResults()
    {
        var ids = Enumerable.Range(1, 150).Select(index => $"id-{index:D3}").ToArray();
        var lookup = new Lookup([], ids.Select(id => Document(id, $"{id}.txt")).ToArray());
        var result = await Search(lookup, new VectorStore(ids.Select(id => Match(id, 0.8)).ToArray()), new Provider())
            .SearchAsync(new SearchRequest("outdoor sleeping"), default);

        Assert.Equal(20, result.Hits.Count);
        Assert.Equal(2, lookup.HydrationCalls);
        Assert.Contains(result.Hits, hit => hit.FileId == "id-001");
    }

    /// <summary>Legacy path-only hits and current stable identities fuse once while retaining literal snippets.</summary>
    [Fact]
    public void Fusion_LegacyPathAndStableId_AreOneHitWithBothRanks()
    {
        var current = Candidate("catalog-id", "same.txt", 0);
        var snippet = new SearchSnippet(SearchSnippetSource.ExtractedText, "native text", "literal evidence", []);
        var legacy = current with { Document = current.Document with { FileId = null }, Snippet = snippet };
        var result = ReciprocalRankFusion.Fuse([legacy], [current.Document], [Match("catalog-id", 0.8)], Model, 10);

        var hit = Assert.Single(result);
        Assert.Equal("catalog-id", hit.Document.FileId);
        Assert.Equal(2d / 61, hit.Score, 12);
        Assert.Same(snippet, hit.Snippet);
    }

    /// <summary>RRF uses rank positions and counts each file once per channel.</summary>
    [Fact]
    public void Fusion_CombinesRanksRatherThanScores_AndCountsOneChunkPerFile()
    {
        var first = Candidate("lexical", "first.txt", 999_999);
        var both = Candidate("both", "both.txt", 1);
        var semantic = Candidate("semantic", "semantic.txt", 0);
        var result = ReciprocalRankFusion.Fuse([first, both], [both.Document, semantic.Document],
            [Match("both", 0.9), Match("both", 0.8) with { ChunkId = "second-chunk" }, Match("semantic", 0.7)], Model, 10);

        Assert.Equal(["both", "lexical", "semantic"], result.Select(item => item.Document.FileId));
        Assert.Equal(1d / 62 + 1d / 61, result[0].Score, 12);
        Assert.Equal(1d / 62, result[2].Score, 12);
        Assert.Single(result[0].Components, component => component.Kind == SearchRankingSignalKind.SemanticSimilarity);
        Assert.Contains("embedding-only", result[0].Components[0].Explanation, StringComparison.Ordinal);
        Assert.Contains("cosine 0.900", result[0].Components[0].Explanation, StringComparison.Ordinal);
        Assert.Contains("characters 12–29", result[0].Components[0].Explanation, StringComparison.Ordinal);
    }

    /// <summary>Exact filename intent remains ahead of a stronger fused discovery score.</summary>
    [Fact]
    public void Fusion_ExactFilenameRemainsFirst_AndOrderingIsStable()
    {
        var exact = Candidate("exact", "tax.pdf", 1) with
        {
            Components = [new(SearchRankingSignalKind.ExactFilename, "filename", 1, "Exact filename match")],
        };
        var both = Candidate("both", "overview.txt", 99999);
        var result = ReciprocalRankFusion.Fuse([exact, both], [both.Document], [Match("both", 0.99)], Model, 2);

        Assert.Equal("exact", result[0].Document.FileId);
        Assert.True(result[1].Score > result[0].Score);
        Assert.Contains(result[1].Components, component => component.Explanation.Contains("k=60", StringComparison.Ordinal));
    }

    /// <summary>Semantic retrieval can discover a file absent from keyword candidates.</summary>
    [Fact]
    public async Task Search_NoKeywordCandidates_ReturnsIndependentSemanticHitAndProvenance()
    {
        var lookup = new Lookup([], [Document("semantic", "camping-guide.txt")]);
        var vectors = new VectorStore([Match("semantic", 0.81)]);
        var result = await Search(lookup, vectors, new Provider()).SearchAsync(new SearchRequest("outdoor sleeping"), default);

        Assert.Equal(SemanticState.Ready, result.State);
        var hit = Assert.Single(result.Hits);
        Assert.Equal("semantic", hit.FileId);
        Assert.Contains("cosine 0.810", hit.Explanation, StringComparison.Ordinal);
        Assert.Contains("chunk chunk-semantic", hit.Explanation, StringComparison.Ordinal);
        Assert.Contains("embedding-only", result.Message, StringComparison.Ordinal);
        Assert.Equal("outdoor sleeping", vectors.LastRequest!.TopicText);
        Assert.Equal(["semantic"], lookup.RequestedIds);
    }

    /// <summary>Filters reach the full-catalog selector and hydration cannot restore excluded files.</summary>
    [Fact]
    public async Task Search_PassesFiltersToIndependentStore_AndRejectsExcludedMissingOrIneligibleHydration()
    {
        var lookup = new Lookup([], [Document("keep", "camping.pdf"), Document("excluded", "excluded.pdf") with { IsExcluded = true },
            Document("wrongtype", "private.txt")]);
        var vectors = new VectorStore([Match("missing", 0.99), Match("excluded", 0.9), Match("wrongtype", 0.8), Match("keep", 0.7)]);
        var result = await Search(lookup, vectors, new Provider()).SearchAsync(new SearchRequest("extension:pdf outdoor sleeping"), default);

        Assert.Equal("keep", Assert.Single(result.Hits).FileId);
        Assert.Contains(vectors.LastRequest!.Filters, filter => filter.Kind == SearchFilterKind.Extension && filter.Value == "pdf");
    }

    /// <summary>Disabled embeddings never contact the model or vector store.</summary>
    [Fact]
    public async Task Search_EmbeddingsDisabled_DoesNotContactProviderOrStore()
    {
        var provider = new Provider();
        var vectors = new VectorStore([]);
        var configuration = new Configuration(false);
        var result = await Search(new Lookup([Document("exact", "invoice.pdf")], []), vectors, provider, configuration)
            .SearchAsync(new SearchRequest("invoice.pdf"), default);

        Assert.Equal("exact", Assert.Single(result.Hits).FileId);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, vectors.SearchCalls);
        Assert.Contains("disabled", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Unavailable or changed models preserve keyword results and report fallback.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("failure")]
    [InlineData("changed")]
    public async Task Search_ModelFailureOrChange_PreservesKeywordResults(string state)
    {
        var provider = new Provider { State = state };
        var vectors = new VectorStore([Match("semantic", 0.99)]);
        var result = await Search(new Lookup([Document("exact", "invoice.pdf")], []), vectors, provider)
            .SearchAsync(new SearchRequest("invoice.pdf"), default);

        Assert.Equal("exact", Assert.Single(result.Hits).FileId);
        Assert.Equal(0, vectors.SearchCalls);
        Assert.Contains("keyword Search continued", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Requested cancellation never becomes a successful fallback result.</summary>
    [Fact]
    public async Task Search_ProviderCancellation_IsCancelledRatherThanFallbackSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new Provider { Cancel = cancellation };
        var result = await Search(new Lookup([Document("exact", "invoice.pdf")], []), new VectorStore([]), provider)
            .SearchAsync(new SearchRequest("invoice.pdf"), cancellation.Token);

        Assert.Equal(SemanticState.Cancelled, result.State);
        Assert.Empty(result.Hits);
    }

    /// <summary>Related suggestions use stored vectors and authoritative current hydration.</summary>
    [Fact]
    public async Task RelatedFiles_UsesStoredVectors_SeparatesSimilarityAndDropsStaleHydration()
    {
        var provider = new Provider();
        var vectors = new VectorStore([Match("seed", 1), Match("other", 0.88), Match("deleted", 0.9), Match("excluded", 0.87)]);
        var lookup = new Lookup([], [Document("other", "tent.txt"), Document("excluded", "excluded.txt") with { IsExcluded = true }]);
        var service = new SemanticRelatedFilesService(new Configuration(), provider, vectors, lookup);
        var result = await service.GetRelatedAsync("seed");

        Assert.Equal("seed", vectors.RelatedSeed);
        Assert.Equal(0, provider.EmbedCalls);
        var suggestion = Assert.Single(result.Files);
        Assert.Equal("other", suggestion.FileId);
        Assert.Contains("Semantic similarity only", suggestion.Explanation, StringComparison.Ordinal);
        Assert.Contains("does not establish a factual relationship", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Disabled or unavailable semantic discovery preserves the direct evidence boundary.</summary>
    [Theory]
    [InlineData(false, "ready")]
    [InlineData(true, "missing")]
    [InlineData(true, "failure")]
    public async Task RelatedFiles_DisabledOrUnavailable_IsVisibleAndReadOnly(bool enabled, string state)
    {
        var provider = new Provider { State = state };
        var vectors = new VectorStore([]);
        var result = await new SemanticRelatedFilesService(new Configuration(enabled), provider, vectors, new Lookup([], []))
            .GetRelatedAsync("seed");

        Assert.Empty(result.Files);
        Assert.Contains("Evidence-backed relationships remain available", result.Message, StringComparison.Ordinal);
        Assert.Null(vectors.RelatedSeed);
        if (!enabled)
        {
            Assert.Equal(0, provider.Calls);
        }
    }

    /// <summary>Final validation drops evidence invalidated while current document details were loading.</summary>
    [Fact]
    public async Task Search_RevalidatesLearnedEvidenceAfterHydration()
    {
        var vectors = new VectorStore([Match("semantic", 0.9)]);
        var lookup = new Lookup([Document("exact", "invoice.pdf")], [Document("semantic", "other.txt")])
        { AfterHydration = () => vectors.Revoked = true };
        var result = await Search(lookup, vectors, new Provider()).SearchAsync(new SearchRequest("invoice.pdf"), default);
        Assert.Equal("exact", Assert.Single(result.Hits).FileId);
        Assert.Equal(1, vectors.ValidationCalls);
    }

    /// <summary>Initial revocation also removes the same file's earlier keyword snapshot, even with no surviving vectors.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Search_InitialFenceDropsRevokedSharedKeywordIdentity(bool retainOtherVector, bool missingDuringHydration)
    {
        var forgotten = Document("forgotten", "invoice.pdf");
        var safe = Document("safe", "invoice-archive.txt");
        var other = Document("semantic", "outdoors.txt");
        var vectors = new VectorStore(retainOtherVector ? [Match("forgotten", 0.9), Match("semantic", 0.8)] : [Match("forgotten", 0.9)]);
        var lookup = new Lookup([forgotten, safe], [forgotten, other]);
        lookup.AfterHydration = () =>
        {
            vectors.RevokedIds.Add("forgotten");
            if (missingDuringHydration) lookup.HiddenIds.Add("forgotten");
        };

        var result = await Search(lookup, vectors, new Provider()).SearchAsync(new SearchRequest("invoice"), default);

        Assert.DoesNotContain(result.Hits, hit => hit.FileId == "forgotten");
        Assert.Contains(result.Hits, hit => hit.FileId == "safe");
        Assert.Equal(retainOtherVector ? 2 : 1, result.Hits.Count);
        Assert.Equal(1, lookup.HydrationCalls);
    }

    /// <summary>Unconfirmed optional failures cannot erase an independent exact keyword hit sharing a vector identity.</summary>
    [Theory]
    [InlineData("hydration")]
    [InlineData("validation")]
    [InlineData("validation-timeout")]
    public async Task Search_OptionalFreshnessFailurePreservesSharedExactKeyword(string failure)
    {
        var exact = Document("exact", "invoice.pdf");
        var lookup = new Lookup([exact], [exact])
        {
            HydrationFailure = failure == "hydration" ? new IOException("optional lookup unavailable") : null,
        };
        var vectors = new VectorStore([Match("exact", 0.9)]) { ValidationFailure = failure };

        var result = await Search(lookup, vectors, new Provider()).SearchAsync(new SearchRequest("invoice.pdf"), default)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("exact", Assert.Single(result.Hits).FileId);
        Assert.DoesNotContain("retained evidence", result.Hits[0].Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("cosine", result.Hits[0].Explanation, StringComparison.Ordinal);
        Assert.Contains("keyword Search continued", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Learned fusion has one semantic vote; compatibility hashing is restored only after learned evidence disappears.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_LearnedFusionExcludesHashVoteAndRestoresLegacyFallback(bool revokeDuringAi)
    {
        var document = Document("semantic", "qzv.txt") with
        {
            SemanticRepresentation = new FeatureHashingEmbeddingProvider().Embed("outdoor sleeping"),
        };
        var vectors = new VectorStore([Match("semantic", 0.9)]);
        var service = Search(new Lookup([document], [document]), vectors, new Provider(),
            assistant: revokeDuringAi ? new InvalidatingAssistant(() => vectors.Revoked = true) : null);

        var result = await service.SearchAsync(new SearchRequest("outdoor sleeping") { UseAiAssistance = revokeDuringAi }, default);

        var hit = Assert.Single(result.Hits);
        if (revokeDuringAi)
        {
            Assert.DoesNotContain("reciprocal", hit.Explanation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("retained evidence", hit.Explanation, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(1d / 61, hit.Score, 12);
            Assert.Contains("keyword rank absent", hit.Explanation, StringComparison.Ordinal);
        }
    }

    /// <summary>A pair rejected during details loading cannot return as a learned suggestion.</summary>
    [Fact]
    public async Task RelatedFiles_RevalidatesPairDecisionsAfterHydration()
    {
        var vectors = new VectorStore([Match("other", 0.9)]);
        var lookup = new Lookup([], [Document("other", "tent.txt")]) { AfterHydration = () => vectors.Revoked = true };
        var result = await new SemanticRelatedFilesService(new Configuration(), new Provider(), vectors, lookup).GetRelatedAsync("seed");
        Assert.Empty(result.Files);
        Assert.Equal(1, vectors.ValidationCalls);
        Assert.Equal("seed", vectors.ValidatedSeed);
    }

    /// <summary>Replacing settings during hydration suppresses results from the old selected model.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_ModelSelectionOrConsentChangeDuringHydrationFallsBack(bool enabled)
    {
        var configuration = new Configuration();
        var lookup = new Lookup([Document("exact", "invoice.pdf")], [Document("semantic", "tent.txt")])
        {
            AfterHydration = () => configuration.Current = new ApplicationSettings
            { SemanticSearch = new SemanticSearchSettings { Enabled = true, EmbeddingsEnabled = enabled, EmbeddingModel = "new-model" } },
        };
        var result = await Search(lookup, new VectorStore([Match("semantic", 0.9)]), new Provider(), configuration)
            .SearchAsync(new SearchRequest("invoice.pdf"), default);
        Assert.Equal("exact", Assert.Single(result.Hits).FileId);
        Assert.Contains("settings changed", result.Message, StringComparison.Ordinal);
    }

    /// <summary>Interactive retrieval has a shared short budget instead of waiting for a chat-sized timeout.</summary>
    [Fact]
    public async Task SearchAndRelated_SlowOptionalProviderFallsBackWithinQueryBudget()
    {
        var provider = new Provider { State = "timeout" };
        var searchTask = Search(new Lookup([Document("exact", "invoice.pdf")], []), new VectorStore([]), provider)
            .SearchAsync(new SearchRequest("invoice.pdf"), default);
        var relatedTask = new SemanticRelatedFilesService(new Configuration(), provider, new VectorStore([]), new Lookup([], []))
            .GetRelatedAsync("seed");
        await Task.WhenAll(searchTask, relatedTask).WaitAsync(TimeSpan.FromSeconds(10));
        var result = await searchTask;
        Assert.Equal("exact", Assert.Single(result.Hits).FileId);
        Assert.Contains("5-second query budget", result.Message, StringComparison.Ordinal);
        Assert.Empty((await relatedTask).Files);
        Assert.Contains("5-second query budget", (await relatedTask).Message, StringComparison.Ordinal);
    }

    /// <summary>Caller cancellation remains distinguishable from an optional semantic budget timeout.</summary>
    [Fact]
    public async Task RelatedFiles_CallerCancellationStillPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new SemanticRelatedFilesService(new Configuration(), new Provider { Cancel = cancellation }, new VectorStore([]), new Lookup([], []));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetRelatedAsync("seed", cancellation.Token));
    }

    /// <summary>Late AI review cannot republish revoked chunks or discard independent keyword matches.</summary>
    [Fact]
    public async Task Search_FinalPublicationFenceRestoresKeywordOnlyAfterRerankInvalidation()
    {
        var exact = Document("exact", "invoice.pdf");
        var vectors = new VectorStore([Match("exact", 0.8), Match("semantic", 0.9)]);
        var lookup = new Lookup([exact], [exact, Document("semantic", "other.txt")]);
        var service = Search(lookup, vectors, new Provider(), assistant: new InvalidatingAssistant(() => vectors.Revoked = true));
        var result = await service.SearchAsync(new SearchRequest("invoice.pdf") { UseAiAssistance = true }, default);
        var hit = Assert.Single(result.Hits);
        Assert.Equal("exact", hit.FileId);
        Assert.DoesNotContain("retained evidence", hit.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("cosine", hit.Explanation, StringComparison.Ordinal);
        Assert.Equal(3, vectors.ValidationCalls);
        Assert.False(result.AiAssistance.WasApplied);
        Assert.Contains("stale semantic matches were removed", result.Message, StringComparison.Ordinal);
    }

    /// <summary>A file forgotten during AI review cannot be restored from an earlier keyword snapshot.</summary>
    [Fact]
    public async Task Search_FinalFenceDoesNotRestoreForgottenKeywordIdentity()
    {
        var forgotten = Document("forgotten", "invoice.pdf");
        var safe = Document("safe", "invoice-archive.txt");
        var vectors = new VectorStore([Match("forgotten", 0.8), Match("semantic", 0.9)]);
        var lookup = new Lookup([forgotten, safe], [forgotten, Document("semantic", "other.txt")]);
        var service = Search(lookup, vectors, new Provider(), assistant: new InvalidatingAssistant(() =>
        {
            vectors.Revoked = true;
            lookup.HiddenIds.Add("forgotten");
        }));
        var result = await service.SearchAsync(new SearchRequest("invoice") { UseAiAssistance = true }, default);
        Assert.Equal("safe", Assert.Single(result.Hits).FileId);
        Assert.Equal(2, lookup.HydrationCalls);
    }

    /// <summary>A second mutation during bounded keyword repair drops newly stale rows without another loop.</summary>
    [Fact]
    public async Task Search_SecondFenceDropsNewlyRevokedKeywordSnapshot()
    {
        var first = Document("a", "invoice-a.txt");
        var second = Document("b", "invoice-b.txt");
        var safe = Document("safe", "invoice-c.txt");
        var vectors = new VectorStore([Match("a", 0.8), Match("b", 0.9)]);
        var lookup = new Lookup([first, second, safe], [first, second]);
        lookup.AfterHydration = () =>
        {
            if (lookup.HydrationCalls > 1)
            {
                vectors.RevokedIds.Add("b");
                lookup.HiddenIds.Add("b");
            }
        };
        var service = Search(lookup, vectors, new Provider(), assistant: new InvalidatingAssistant(() =>
        {
            vectors.RevokedIds.Add("a");
            lookup.HiddenIds.Add("a");
        }));
        var result = await service.SearchAsync(new SearchRequest("invoice") { UseAiAssistance = true }, default);
        Assert.Equal("safe", Assert.Single(result.Hits).FileId);
        Assert.Equal(2, lookup.HydrationCalls);
        Assert.Equal(3, vectors.ValidationCalls);
    }

    private static SemanticSearchService Search(Lookup lookup, VectorStore vectors, Provider provider, Configuration? configuration = null,
        IAiSearchAssistant? assistant = null) =>
        new(configuration ?? new Configuration(), new FeatureHashingEmbeddingProvider(), new EmptyStore(), lookup,
            aiSearchAssistant: assistant, modelEmbeddingProvider: provider, vectorSearchStore: vectors);

    private static ProgressiveSearchDocument Document(string id, string name) => new()
    {
        FileId = id,
        FileName = name,
        FullPath = Path.Combine(Path.GetTempPath(), "OmniSorSe-vector-test", name),
        RelativePath = name,
        FolderName = "OmniSorSe-vector-test",
        Extension = Path.GetExtension(name),
        IsFullyIndexed = true,
    };

    private static RankedSearchCandidate Candidate(string id, string name, double score) => new(
        new SearchCandidateDocument { FileId = id, FileName = name, FullPath = name }, score, [], null);

    private static VectorSearchMatch Match(string id, double score) => new(id, $"chunk-{id}", score, "extracted text", 12, 17, "retained evidence");

    private sealed class Configuration(bool enabled = true) : IConfigurationService
    {
        public ApplicationSettings Current { get; set; } = new()
        {
            SemanticSearch = new SemanticSearchSettings { Enabled = true, EmbeddingsEnabled = enabled, MaximumResultCount = 20 },
        };
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Provider : IModelEmbeddingProvider
    {
        public int Calls { get; private set; }
        public int EmbedCalls { get; private set; }
        public string State { get; init; } = "ready";
        public CancellationTokenSource? Cancel { get; init; }

        public Task<SemanticModelIdentity?> GetModelAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Cancel is not null)
            {
                Cancel.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return State switch
            {
                "missing" => Task.FromResult<SemanticModelIdentity?>(null),
                "failure" => Task.FromException<SemanticModelIdentity?>(new IOException("offline")),
                "timeout" => WaitForCancellationAsync(cancellationToken),
                _ => Task.FromResult<SemanticModelIdentity?>(Model),
            };
        }

        private static async Task<SemanticModelIdentity?> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }

        public Task<ModelEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, SemanticModelIdentity? expectedModel = null, CancellationToken cancellationToken = default)
        {
            EmbedCalls++;
            Assert.Equal(Model, expectedModel);
            return Task.FromResult(new ModelEmbeddingResult(State == "changed" ? Model with { Revision = "digest-2" } : Model, [[1, 0]]));
        }
    }

    private sealed class Lookup(IReadOnlyList<ProgressiveSearchDocument> lexical, IReadOnlyList<ProgressiveSearchDocument> documents)
        : IProgressiveSearchSource, IProgressiveSearchDocumentLookup
    {
        public IReadOnlyList<string> RequestedIds { get; private set; } = [];
        public int HydrationCalls { get; private set; }
        public Action? AfterHydration { get; set; }
        public Exception? HydrationFailure { get; init; }
        public HashSet<string> HiddenIds { get; } = new(StringComparer.Ordinal);
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(int maximumCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(lexical);
        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(IReadOnlyList<string> fileIds, CancellationToken cancellationToken = default)
        {
            Assert.InRange(fileIds.Count, 1, RelationshipLimits.MaximumSearchExpansions);
            HydrationCalls++;
            RequestedIds = fileIds;
            if (HydrationFailure is not null) return Task.FromException<IReadOnlyList<ProgressiveSearchDocument>>(HydrationFailure);
            AfterHydration?.Invoke();
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(documents.Where(document =>
                fileIds.Contains(document.FileId) && !HiddenIds.Contains(document.FileId)).ToArray());
        }
        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SearchCoverage(100, 100, 0, 0, 0, 100));
        public Task<IReadOnlyList<string>> GetExcludedPathsAsync(int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class VectorStore(IReadOnlyList<VectorSearchMatch> matches) : IVectorSearchStore
    {
        public int SearchCalls { get; private set; }
        public DiscoverySearchRequest? LastRequest { get; private set; }
        public string? RelatedSeed { get; private set; }
        public bool Revoked { get; set; }
        public HashSet<string> RevokedIds { get; } = new(StringComparer.Ordinal);
        public int ValidationCalls { get; private set; }
        public string? ValidatedSeed { get; private set; }
        public string? ValidationFailure { get; init; }
        public Task<IReadOnlyList<VectorSearchMatch>> ValidateVectorMatchesAsync(SemanticModelIdentity model,
            VectorSearchResult result, DiscoverySearchRequest request, string? relatedFileId = null, CancellationToken cancellationToken = default)
        {
            ValidationCalls++;
            ValidatedSeed = relatedFileId;
            if (ValidationFailure == "validation")
                return Task.FromException<IReadOnlyList<VectorSearchMatch>>(new IOException("optional validation unavailable"));
            if (ValidationFailure == "validation-timeout") return WaitForValidationTimeoutAsync(cancellationToken);
            return Task.FromResult<IReadOnlyList<VectorSearchMatch>>(Revoked ? [] : result.Matches.Where(match => !RevokedIds.Contains(match.FileId)).ToArray());
        }
        private static async Task<IReadOnlyList<VectorSearchMatch>> WaitForValidationTimeoutAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
        public Task<VectorSearchResult> SearchVectorsAsync(SemanticModelIdentity model, IReadOnlyList<float> query, DiscoverySearchRequest request, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            LastRequest = request;
            return Task.FromResult(new VectorSearchResult(matches, 100, 80, false));
        }
        public Task<VectorSearchResult> SearchRelatedVectorsAsync(SemanticModelIdentity model, string fileId, DiscoverySearchRequest request, CancellationToken cancellationToken = default)
        {
            RelatedSeed = fileId;
            return Task.FromResult(new VectorSearchResult(matches, 100, 80, false));
        }
        public Task<IReadOnlyList<VectorIndexDocument>> GetVectorIndexCandidatesAsync(SemanticModelIdentity model, int maximumCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReplaceVectorsAsync(VectorIndexDocument document, SemanticModelIdentity model, IReadOnlyList<VectorChunkEmbedding> chunks, long maximumBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<VectorStorageStatus> GetVectorStatusAsync(SemanticModelIdentity model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearVectorsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PruneVectorsAsync(SemanticModelIdentity model, long maximumBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyStore : ISemanticIndexStore
    {
        public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SemanticIndexEntry>>([]);
        public Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InvalidatingAssistant(Action invalidate) : IAiSearchAssistant
    {
        public async Task<AiSearchRerankResult> RerankAsync(SearchInterpretation interpretation,
            IReadOnlyList<RankedSearchCandidate> candidates, AiSettings settings, CancellationToken cancellationToken)
        {
            Assert.Contains(candidates, candidate => candidate.Components.Any(component => component.Kind == SearchRankingSignalKind.HybridFusion));
            await Task.Yield();
            invalidate();
            return new(candidates.Reverse().ToArray(), new(AiSearchAssistanceState.Applied, "Reordered", candidates.Count, true));
        }
    }
}
