using System.Net;
using System.Text;
using System.Text.Json;
using OpenSorSe.AI;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Exercises untrusted inference parsing and the bounded production local-AI transport.</summary>
public sealed class IndexingEnrichmentTests
{
    private const string Valid = """
        {"summary":"Electricity statement from EnBW.","documentType":"bill","category":"finance",
        "tags":["electricity","utility"],"topics":["household energy"],"entities":[{"kind":"Organization","name":"EnBW"}]}
        """;

    /// <summary>Semantic labels need not occur literally in the source and remain explicitly AI-derived.</summary>
    [Fact]
    public void ValidInferenceCarriesProvenanceAndSearchableConcepts()
    {
        var result = IndexingEnrichmentValidator.Parse(Valid, "model-v1");
        Assert.Equal(ContentIntelligenceOrigin.AiDerived, result.Origin);
        Assert.Equal("bill", result.DocumentType);
        Assert.Equal("finance", result.Category);
        Assert.Contains("EnBW", result.Keywords);
        Assert.All(result.Topics.Concat(result.Entities), concept =>
        {
            Assert.Equal(ContentIntelligenceOrigin.AiDerived, concept.Origin);
            Assert.Equal("model-v1", concept.ProviderVersion);
            Assert.Equal(ContentIntelligenceConfidence.Limited, concept.Confidence);
        });
        Assert.Equal(ContentIntelligenceOrigin.AiDerived, result.Summary!.Origin);
    }

    /// <summary>Search presents model summaries as inference, while deterministic summaries retain their source-grounded label.</summary>
    [Theory]
    [InlineData(ContentIntelligenceOrigin.AiDerived, "AI-derived summary")]
    [InlineData(ContentIntelligenceOrigin.Deterministic, "Source-grounded summary")]
    public void SearchSummaryExplanationAndSnippetRespectProvenance(ContentIntelligenceOrigin origin, string label)
    {
        var candidate = new SearchCandidateDocument
        {
            FileId = "record",
            FullPath = Path.Combine(Path.GetTempPath(), "opaque.txt"),
            FileName = "opaque.txt",
            RelativePath = "opaque.txt",
            Extension = ".txt",
            FileType = "document",
            ContentIntelligence = new IndexedContentIntelligence
            {
                Origin = origin,
                Provider = "test",
                ProviderVersion = "1",
                ProcessingFingerprint = "test",
                Summary = new ContentSummaryEvidence { Text = "Electricity statement", Provider = "test", ProviderVersion = "1", Origin = origin },
            },
        };
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());

        var result = Assert.Single(ranker.Rank(new SearchInterpretation("electricity", "electricity", ["electricity"], []),
            [candidate], 10, CancellationToken.None));

        Assert.Contains(result.Components, component => component.Kind == SearchRankingSignalKind.ContentIntelligenceSummary &&
            component.Explanation == label + " matched");
        Assert.StartsWith(label + ":", result.Snippet!.AccessibleText, StringComparison.Ordinal);
        if (origin == ContentIntelligenceOrigin.AiDerived)
        {
            Assert.DoesNotContain(result.Components, component => component.Explanation.Contains("source-grounded", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Entity matches distinguish model inference from deterministic entity extraction.</summary>
    [Theory]
    [InlineData(ContentIntelligenceOrigin.AiDerived, "AI-derived entity", "AI-derived entity")]
    [InlineData(ContentIntelligenceOrigin.Deterministic, "textual entity", "Textual entity")]
    public void SearchEntityExplanationAndSnippetRespectProvenance(ContentIntelligenceOrigin origin, string componentLabel, string snippetLabel)
    {
        var inference = IndexingEnrichmentValidator.Parse(Valid, "model-v1");
        var candidate = new SearchCandidateDocument
        {
            FileId = "record",
            FullPath = Path.Combine(Path.GetTempPath(), "opaque.txt"),
            FileName = "opaque.txt",
            RelativePath = "opaque.txt",
            Extension = ".txt",
            FileType = "document",
            ContentIntelligence = inference with
            {
                Origin = origin,
                Summary = null,
                Keywords = [],
                Topics = [],
                Entities = inference.Entities.Select(item => item with { Origin = origin }).ToArray(),
            },
        };
        var ranker = new HybridSearchRanker(new FeatureHashingEmbeddingProvider(), new SearchSnippetFactory());

        var result = Assert.Single(ranker.Rank(new SearchInterpretation("enbw", "enbw", ["enbw"], []),
            [candidate], 10, CancellationToken.None));

        Assert.Contains(result.Components, component => component.Kind == SearchRankingSignalKind.ContentEntity &&
            component.Explanation == componentLabel + " matched");
        Assert.StartsWith(snippetLabel + ":", result.Snippet!.AccessibleText, StringComparison.Ordinal);
    }

    /// <summary>Malformed, duplicate, path-like and unsupported output never reaches durable metadata.</summary>
    [Theory]
    [InlineData("\"category\":\"finance\"", "\"category\":\"finance\",\"category\":\"secret\"")]
    [InlineData("\"utility\"", "\"../secret\"")]
    [InlineData("\"utility\"", "\"ELECTRICITY\"")]
    [InlineData("\"Organization\"", "\"MoveFile\"")]
    [InlineData("\"bill\"", "42")]
    [InlineData("\"finance\"", "null")]
    public void InvalidModelStructuresAreRejected(string original, string replacement)
    {
        Assert.Throws<InvalidDataException>(() => IndexingEnrichmentValidator.Parse(
            Valid.Replace(original, replacement, StringComparison.Ordinal), "model-v1"));
    }

    /// <summary>The provider uses supplied extraction only; a source path is never opened or sent to the model.</summary>
    [Fact]
    public async Task ProductionProviderEnrichesRetainedContentWithoutFileAccess()
    {
        using var handler = new ResponseHandler(JsonSerializer.Serialize(new { done = true, response = Valid }));
        using var client = new HttpClient(handler);
        var provider = new OllamaIndexingEnrichmentProvider(client, new Configuration());

        var result = await provider.EnrichAsync("C:/not-a-real-file/private-name.txt", "Retained statement text.");

        Assert.Equal("bill", result.Intelligence?.DocumentType);
        Assert.DoesNotContain("private-name", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("Retained statement text", handler.RequestBody, StringComparison.Ordinal);
        Assert.Equal("/api/generate", handler.RequestUri?.AbsolutePath);
    }

    /// <summary>Background content cannot leave the configured local loopback endpoint.</summary>
    [Fact]
    public async Task NonLocalEndpointIsUnavailableAndMakesNoRequest()
    {
        using var handler = new ResponseHandler("{}");
        using var client = new HttpClient(handler);
        var config = new Configuration(new AiSettings
        {
            Enabled = true,
            DocumentTextInterpretationEnabled = true,
            SelectedModel = "test-model",
            Endpoint = "https://example.com",
        });
        var provider = new OllamaIndexingEnrichmentProvider(client, config);
        Assert.False(await provider.IsAvailableAsync());
        Assert.Null(handler.RequestUri);
    }

    /// <summary>Cancellation remains cancellation rather than a successful empty inference.</summary>
    [Fact]
    public async Task ProviderPropagatesCallerCancellation()
    {
        using var handler = new ResponseHandler("{}");
        using var client = new HttpClient(handler);
        var provider = new OllamaIndexingEnrichmentProvider(client, new Configuration());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EnrichAsync("name", "text", cancelled.Token));
    }

    private sealed class ResponseHandler(string response) : HttpMessageHandler
    {
        internal string RequestBody { get; private set; } = string.Empty;
        internal Uri? RequestUri { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Configuration(AiSettings? ai = null) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            Ai = ai ?? new AiSettings { Enabled = true, DocumentTextInterpretationEnabled = true, SelectedModel = "test-model" },
        };
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }
}
