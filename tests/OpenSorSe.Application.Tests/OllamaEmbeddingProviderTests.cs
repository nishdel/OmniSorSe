using System.Net;
using System.Text;
using System.Text.Json;
using OpenSorSe.AI;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies local learned embeddings independently from chat settings or an installed Ollama runtime.</summary>
public sealed class OllamaEmbeddingProviderTests
{
    private const string ModelName = "qwen3-embedding:4b";
    private const string Digest = "installed-model-digest";

    /// <summary>Verifies the dedicated model, exact input ordering, no truncation, and normalized vectors.</summary>
    [Fact]
    public async Task EmbedAsync_UsesDedicatedModelAndNormalizesWithoutChatPermission()
    {
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Tags();
            }

            Assert.Equal("/api/embed", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(ModelName, body.RootElement.GetProperty("model").GetString());
            Assert.False(body.RootElement.GetProperty("truncate").GetBoolean());
            Assert.Equal(["first chunk", "second chunk"], body.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()));
            return Embeddings("[[3,4],[0,-5]]");
        }));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(["first chunk", "second chunk"]);

        Assert.True(result.IsAvailable);
        Assert.Equal(new SemanticModelIdentity("ollama", ModelName, Digest, 2), result.Model);
        Assert.Equal(0.6f, result.Vectors[0][0], 5);
        Assert.Equal(0.8f, result.Vectors[0][1], 5);
        Assert.Equal([0f, -1f], result.Vectors[1]);
    }

    /// <summary>Verifies availability rechecks tag digests while reusing only compatible discovered dimensions.</summary>
    [Fact]
    public async Task GetModelAsync_CachesDimensionsButDetectsDigestReplacement()
    {
        var digest = Digest;
        var posts = 0;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(Tags(digest));
            }

            posts++;
            return Task.FromResult(Embeddings(posts == 1 ? "[[3,4]]" : "[[1,2,3]]"));
        }));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var original = await provider.GetModelAsync();
        Assert.Equal(original, await provider.GetModelAsync());
        Assert.Equal(1, posts);
        digest = "replacement-digest";
        var replacement = await provider.GetModelAsync();

        Assert.NotNull(replacement);
        Assert.NotEqual(original!.Key, replacement.Key);
        Assert.Equal(3, replacement.Dimensions);
        Assert.Equal(2, posts);
    }

    /// <summary>Verifies a missing or disabled local model never causes a download or remote request.</summary>
    [Theory]
    [InlineData(false, true, "http://127.0.0.1:11434")]
    [InlineData(true, false, "http://127.0.0.1:11434")]
    [InlineData(true, true, "https://example.test/ollama")]
    [InlineData(true, true, "http://user:secret@localhost:11434")]
    public async Task EmbedAsync_DisabledOrNonLocal_DoesNotSend(bool searchEnabled, bool embeddingsEnabled, string endpoint)
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Tags());
        }));
        var settings = new Settings { Current = Configuration(searchEnabled, embeddingsEnabled, endpoint) };
        var provider = new OllamaEmbeddingProvider(client, settings);

        Assert.Null(await provider.GetModelAsync());
        Assert.False((await provider.EmbedAsync(["example"])).IsAvailable);
        Assert.Equal(0, calls);
    }

    /// <summary>Verifies absent installed models and missing model revisions are unavailable without invoking inference.</summary>
    [Theory]
    [InlineData("{\"models\":[]}")]
    [InlineData("{\"models\":[{\"name\":\"qwen3-embedding:4b\"}]}")]
    [InlineData("{\"models\":[{\"name\":\"qwen3-embedding:4b\",\"digest\":\"\"}]}")]
    [InlineData("[]")]
    public async Task GetModelAsync_InvalidOrMissingInstalledIdentity_IsUnavailable(string response)
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(response));
        }));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        Assert.Null(await provider.GetModelAsync());
        Assert.False((await provider.EmbedAsync(["example"])).IsAvailable);
    }

    /// <summary>Verifies tags replaced during inference never publish vectors in the old space.</summary>
    [Fact]
    public async Task EmbedAsync_ModelChangesDuringResponse_DiscardsVectors()
    {
        var tags = 0;
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? Tags(++tags == 1 ? Digest : "replacement-digest")
            : Embeddings("[[1,2]]"))));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(["example"], new SemanticModelIdentity("ollama", ModelName, Digest, 2));

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Vectors);
        Assert.Contains("changed", result.UnavailableReason);
    }

    /// <summary>Verifies previously indexed model identity prevents even starting an incompatible inference request.</summary>
    [Fact]
    public async Task EmbedAsync_ExpectedDigestDiffers_DoesNotEmbed()
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Tags("replacement-digest"));
        }));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(["example"], new SemanticModelIdentity("ollama", ModelName, Digest, 2));

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Vectors);
    }

    /// <summary>Verifies changing explicit consent while inference runs discards the result.</summary>
    [Fact]
    public async Task EmbedAsync_SettingsDisabledDuringRequest_DiscardsVectors()
    {
        var settings = new Settings();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(Tags());
            }

            settings.Current = Configuration(embeddingsEnabled: false);
            return Task.FromResult(Embeddings("[[1,2]]"));
        }));
        var provider = new OllamaEmbeddingProvider(client, settings);

        Assert.False((await provider.EmbedAsync(["example"])).IsAvailable);
    }

    /// <summary>Verifies malformed and non-finite response vectors fail closed through the public transport seam.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[[]]")]
    [InlineData("[[0,0]]")]
    [InlineData("[[1,2],[3,4]]")]
    [InlineData("[[1e100,2]]")]
    [InlineData("[[\"NaN\",2]]")]
    [InlineData("[[null,2]]")]
    [InlineData("[null]")]
    [InlineData("{}")]
    public async Task EmbedAsync_InvalidVectors_AreUnavailable(string vectors)
    {
        using var client = ValidClient(vectors);
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(["example"]);

        Assert.False(result.IsAvailable);
        Assert.True(result.IsDocumentFailure);
        Assert.Empty(result.Vectors);
    }

    /// <summary>Verifies dimension consistency within a batch and against a previously resolved model.</summary>
    [Fact]
    public async Task EmbedAsync_InconsistentDimensions_AreUnavailable()
    {
        using var client = ValidClient("[[1,2],[3,4,5]]");
        var provider = new OllamaEmbeddingProvider(client, new Settings());
        Assert.False((await provider.EmbedAsync(["one", "two"])).IsAvailable);

        using var otherClient = ValidClient("[[1,2,3]]");
        var other = new OllamaEmbeddingProvider(otherClient, new Settings());
        Assert.False((await other.EmbedAsync(["one"], new SemanticModelIdentity("ollama", ModelName, Digest, 2))).IsAvailable);
    }

    /// <summary>Verifies response length and dimensional bounds independently from source text limits.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmbedAsync_OverlargeResponseOrVector_IsUnavailable(bool tooManyDimensions)
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(Tags());
            }

            var response = tooManyDimensions
                ? Embeddings("[[" + string.Join(',', Enumerable.Repeat("1", OllamaEmbeddingProvider.MaximumDimensions + 1)) + "]]")
                : Json(new string(' ', OllamaTransportLimits.MaximumResponseBytes + 1));
            return Task.FromResult(response);
        }));
        var provider = new OllamaEmbeddingProvider(client, new Settings());
        Assert.False((await provider.EmbedAsync(["example"])).IsAvailable);
    }

    /// <summary>Verifies streamed responses cannot bypass the byte bound by omitting Content-Length.</summary>
    [Fact]
    public async Task EmbedAsync_OversizedResponseWithoutContentLength_IsUnavailable()
    {
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? Tags()
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(new byte[OllamaTransportLimits.MaximumResponseBytes + 1]),
            })));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(["example"]);

        Assert.False(result.IsAvailable);
        Assert.True(result.IsDocumentFailure);
    }

    /// <summary>Verifies provider cancellation is optional failure while caller cancellation remains observable.</summary>
    [Fact]
    public async Task EmbedAsync_CallerCancellationPropagatesAndTransportTimeoutIsUnavailable()
    {
        using var cancelled = new CancellationTokenSource();
        using var client = new HttpClient(new Handler((_, _) => throw new OperationCanceledException()));
        var provider = new OllamaEmbeddingProvider(client, new Settings());
        Assert.False((await provider.EmbedAsync(["example"])).IsAvailable);
        Assert.Null(await provider.GetModelAsync());

        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EmbedAsync(["example"], cancellationToken: cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetModelAsync(cancelled.Token));
    }

    /// <summary>Verifies cancellation reaches an active HTTP request rather than being reported as optional unavailability.</summary>
    [Fact]
    public async Task EmbedAsync_CancellationDuringTransport_Propagates()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Tags();
        }));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var request = provider.EmbedAsync(["example"], cancellationToken: cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    /// <summary>Verifies encoded JSON expansion is bounded before any text is submitted for inference.</summary>
    [Fact]
    public async Task EmbedAsync_JsonEscapingExceedsByteBound_DoesNotPost()
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Tags());
        }));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(Enumerable.Repeat(new string('\u263a', 1600), 16).ToArray());

        Assert.False(result.IsAvailable);
        Assert.True(result.IsDocumentFailure);
        Assert.Contains("byte bound", result.UnavailableReason);
    }

    /// <summary>Distinguishes batch rejection from provider-wide failure without charging files for an invalid model catalog.</summary>
    [Theory]
    [InlineData(400, false, true)]
    [InlineData(422, false, true)]
    [InlineData(503, false, false)]
    [InlineData(302, false, false)]
    [InlineData(400, true, false)]
    public async Task EmbedAsync_HttpFailuresHaveBoundedRetryScope(int status, bool failTags, bool documentFailure)
    {
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get && !failTags ? Tags() : new HttpResponseMessage((HttpStatusCode)status))));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(["example"]);

        Assert.False(result.IsAvailable);
        Assert.Equal(documentFailure, result.IsDocumentFailure);
        Assert.Empty(result.Vectors);
    }

    /// <summary>Verifies a malformed tags response after inference stays a service failure rather than exhausting a file retry.</summary>
    [Fact]
    public async Task EmbedAsync_InvalidCatalogAfterInference_IsGlobalFailure()
    {
        var tags = 0;
        using var client = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? ++tags == 1 ? Tags() : Json("[]")
            : Embeddings("[[1,2]]"))));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        var result = await provider.EmbedAsync(["example"]);

        Assert.False(result.IsAvailable);
        Assert.False(result.IsDocumentFailure);
        Assert.Empty(result.Vectors);
    }

    /// <summary>Verifies invalid model declarations and malformed envelopes cannot publish vectors.</summary>
    [Theory]
    [InlineData("{\"model\":\"other-model\",\"embeddings\":[[1,2]]}")]
    [InlineData("{\"embeddings\":[[1,2]]}")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task EmbedAsync_InvalidEnvelope_IsUnavailable(string body)
    {
        using var client = new HttpClient(new Handler((request, _) =>
            Task.FromResult(request.Method == HttpMethod.Get ? Tags() : Json(body))));
        var provider = new OllamaEmbeddingProvider(client, new Settings());

        Assert.False((await provider.EmbedAsync(["example"])).IsAvailable);
    }

    /// <summary>Verifies rejected input bounds are programmer errors before any local inference activity.</summary>
    [Fact]
    public async Task EmbedAsync_InputBoundsRejectBeforeTransport()
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("No requests expected.")));
        var provider = new OllamaEmbeddingProvider(client, new Settings());
        await Assert.ThrowsAsync<ArgumentException>(() => provider.EmbedAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.EmbedAsync([" "]));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.EmbedAsync([new string('x', 1601)]));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.EmbedAsync(Enumerable.Repeat("input", 17).ToArray()));
    }

    private static HttpClient ValidClient(string vectors) => new(new Handler((request, _) =>
        Task.FromResult(request.Method == HttpMethod.Get ? Tags() : Embeddings(vectors))));

    private static HttpResponseMessage Tags(string digest = Digest) => Json(JsonSerializer.Serialize(new
    {
        models = new[] { new { name = ModelName, digest } },
    }));

    private static HttpResponseMessage Embeddings(string vectors) => Json("{\"model\":\"" + ModelName + "\",\"embeddings\":" + vectors + "}");

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private static ApplicationSettings Configuration(bool searchEnabled = true, bool embeddingsEnabled = true,
        string endpoint = "http://127.0.0.1:11434/api/embed") => new()
        {
            SemanticSearch = new SemanticSearchSettings { Enabled = searchEnabled, EmbeddingsEnabled = embeddingsEnabled, EmbeddingModel = ModelName },
            Ai = new AiSettings { Enabled = false, SelectedModel = "chat-only-model", Endpoint = endpoint },
        };

    private sealed class Settings : IConfigurationService
    {
        public ApplicationSettings Current { get; set; } = Configuration();
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
