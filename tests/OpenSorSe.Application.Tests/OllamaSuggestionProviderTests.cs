using System.Net;
using System.Text;
using System.Text.Json;
using OpenSorSe.AI;
using OpenSorSe.Application.AI;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

/// <summary>
/// Verifies the Ollama HTTP implementation with mocked transport and no installed Ollama dependency.
/// </summary>
public sealed class OllamaSuggestionProviderTests
{
    /// <summary>
    /// Verifies a reachable provider discovers installed models through its typed transport contract.
    /// </summary>
    [Fact]
    public async Task GetConnectionAsync_ReachableProvider_ReturnsDiscoveredModels()
    {
        using var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, """{"models":[{"name":"llama3:latest"},{"name":"mistral"}]}""")));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GetConnectionAsync(EnabledSettings(), CancellationToken.None);

        Assert.Equal(AiAvailabilityState.Connected, result.State);
        Assert.Equal(["llama3:latest", "mistral"], result.Models.Select(model => model.Id));
    }

    /// <summary>Verifies the dedicated connection check uses version without discovering or selecting models.</summary>
    [Fact]
    public async Task CheckConnectionAsync_ReachableProvider_UsesVersionEndpointOnly()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/api/version", request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, """{"version":"0.9.0"}""");
        }));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.CheckConnectionAsync(EnabledSettings(), CancellationToken.None);

        Assert.Equal(AiAvailabilityState.Connected, result.State);
        Assert.Equal("0.9.0", result.ProviderVersion);
        Assert.Empty(result.Models);
    }

    /// <summary>Verifies connection diagnostics preserve the provider's exact bounded response facts.</summary>
    [Fact]
    public async Task CheckConnectionAsync_DiagnosticsEnabled_CapturesActualTransportFacts()
    {
        const string body = """{"version":"0.9.0"}""";
        var collector = new AiDiagnosticsCollector();
        collector.Configure(true, true);
        var id = collector.Begin(
            AiSuggestionKind.ConnectionTest,
            string.Empty,
            "http://127.0.0.1:11434")!;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            var response = Json(HttpStatusCode.OK, body);
            response.Headers.Server.ParseAdd("Ollama/Test");
            return response;
        }));
        var provider = new OllamaSuggestionProvider(client, new LoggingService(), collector);

        var result = await provider.CheckConnectionAsync(
            EnabledSettings(),
            id,
            CancellationToken.None);

        var session = Assert.Single(collector.GetRecent());
        Assert.Equal(AiAvailabilityState.Connected, result.State);
        Assert.Equal(body, session.RawHttpResponse);
        Assert.Equal(HttpStatusCode.OK, (HttpStatusCode?)session.HttpStatusCode);
        Assert.Equal("application/json; charset=utf-8", session.ContentType);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), session.ResponseSizeBytes);
        Assert.True(session.ResponseComplete);
        Assert.False(session.WasStreaming);
        Assert.Equal("Ollama/Test", session.SafeResponseHeaders["Server"]);
    }

    /// <summary>Verifies provider-controlled model names cannot inject controls or unbounded text into UI and logs.</summary>
    [Fact]
    public async Task GetConnectionAsync_InvalidModelIdentifiers_AreExcluded()
    {
        var oversized = new string('m', AiSettings.MaximumModelIdentifierLength + 1);
        var payload = $$"""{"models":[{"name":"valid"},{"name":"bad\nname"},{"name":"{{oversized}}"}]}""";
        using var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, payload)));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GetConnectionAsync(EnabledSettings(), CancellationToken.None);

        Assert.Equal(["valid"], result.Models.Select(model => model.Id));
    }

    /// <summary>Verifies a provider cannot publish an unbounded number of models to Desktop state.</summary>
    [Fact]
    public async Task GetConnectionAsync_LargeValidModelList_IsDeterministicallyBounded()
    {
        var payload = JsonSerializer.Serialize(new
        {
            models = Enumerable.Range(0, OllamaTransportLimits.MaximumPublishedModelCount + 1)
                .Reverse()
                .Select(index => new { name = $"model-{index:D3}" }),
        });
        using var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, payload)));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GetConnectionAsync(EnabledSettings(), CancellationToken.None);

        Assert.Equal(OllamaTransportLimits.MaximumPublishedModelCount, result.Models.Count);
        Assert.Equal("model-000", result.Models[0].Id);
        Assert.Equal("model-099", result.Models[^1].Id);
    }

    /// <summary>Verifies an oversized model response is rejected before JSON materialization.</summary>
    [Fact]
    public async Task GetConnectionAsync_OversizedResponse_ReturnsUnavailable()
    {
        var payload = new string(' ', OllamaTransportLimits.MaximumResponseBytes + 1);
        using var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, payload)));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GetConnectionAsync(EnabledSettings(), CancellationToken.None);

        Assert.Equal(AiAvailabilityState.Unavailable, result.State);
        Assert.Empty(result.Models);
    }

    /// <summary>
    /// Verifies no installed model is a clear nonfatal provider state.
    /// </summary>
    [Fact]
    public async Task GetConnectionAsync_NoModels_ReturnsNoModelsState()
    {
        using var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, """{"models":[]}""")));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GetConnectionAsync(EnabledSettings(), CancellationToken.None);

        Assert.Equal(AiAvailabilityState.NoModelsAvailable, result.State);
        Assert.Empty(result.Models);
    }

    /// <summary>
    /// Verifies malformed model output and HTTP errors remain isolated from core application behavior.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "{invalid")]
    public async Task GetConnectionAsync_InvalidOrFailedResponse_ReturnsUnavailable(HttpStatusCode statusCode, string body)
    {
        using var client = new HttpClient(new StubHandler(_ => Json(statusCode, body)));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GetConnectionAsync(EnabledSettings(), CancellationToken.None);

        Assert.Equal(AiAvailabilityState.Unavailable, result.State);
    }

    /// <summary>
    /// Verifies valid structured generation is returned as raw JSON for application-owned parsing and never exposes transport DTOs.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ValidResponse_ReturnsStructuredJson()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/generate", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """{"response":"{\"fileName\":null}"}""");
        }));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GenerateAsync(new AiProviderGenerationRequest(
            AiSuggestionKind.FileRename,
            "http://127.0.0.1:11434/api/tags",
            "llama3:latest",
            "redacted test prompt",
            TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("{\"fileName\":null}", result.StructuredJson);
        using var requestDocument = JsonDocument.Parse(result.Diagnostics!.RequestJson);
        var requestRoot = requestDocument.RootElement;
        Assert.Equal(
            AiStructuredOutputContracts.GetSystemPrompt(AiSuggestionKind.FileRename),
            requestRoot.GetProperty("system").GetString());
        Assert.Equal("redacted test prompt", requestRoot.GetProperty("prompt").GetString());
        Assert.False(requestRoot.GetProperty("stream").GetBoolean());
        Assert.Equal("5m", requestRoot.GetProperty("keep_alive").GetString());
        Assert.Equal(0.0, requestRoot.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal(
            AiStructuredOutputContracts.GetSchemaJson(AiSuggestionKind.FileRename),
            requestRoot.GetProperty("format").GetRawText());
        Assert.Equal("""{"response":"{\"fileName\":null}"}""", result.Diagnostics.RawHttpResponse);
        Assert.Equal("{\"fileName\":null}", result.Diagnostics.ExtractedAssistantResponse);
    }

    /// <summary>Verifies the live collector separates the exact payload, raw envelope, and assistant content.</summary>
    [Fact]
    public async Task GenerateAsync_DiagnosticsEnabled_CapturesActualTransportArtifacts()
    {
        var collector = new AiDiagnosticsCollector();
        collector.Configure(true, true);
        var id = collector.Begin(AiSuggestionKind.FileRename, "model", "http://127.0.0.1:11434")!;
        using var logging = new LoggingService();
        logging.Initialize(Microsoft.Extensions.Logging.LogLevel.Trace);
        string? sent = null;
        using var client = new HttpClient(new StubHandler(async (request, cancellationToken) =>
        {
            sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(HttpStatusCode.OK, """{"response":"{\"taskId\":\"file-rename-v2\",\"status\":\"no_suggestion\",\"reason\":\"No change\"}"}""");
        }));
        var provider = new OllamaSuggestionProvider(client, logging, collector);
        var request = new AiProviderGenerationRequest(
            AiSuggestionKind.FileRename, "http://127.0.0.1:11434", "model", """{"input":"final"}""",
            TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds))
        { DiagnosticRequestId = id };

        var result = await provider.GenerateAsync(request, CancellationToken.None);

        var session = Assert.Single(collector.GetRecent());
        Assert.Equal(sent, session.RequestJson);
        Assert.Equal(result.Diagnostics!.RawHttpResponse, session.RawHttpResponse);
        Assert.Equal(result.StructuredJson, session.ExtractedAssistantResponse);
        Assert.Contains(session.Stages, stage => stage.Name == "Response headers received");
        Assert.DoesNotContain(logging.GetRecentEvents(), item =>
            item.Summary.Contains("""{"input":"final"}""", StringComparison.Ordinal) ||
            item.Summary.Contains("No change", StringComparison.Ordinal) ||
            item.ExceptionSummary?.Contains("No change", StringComparison.Ordinal) == true);
    }

    /// <summary>Verifies model and structured-output HTTP failures receive distinct actionable outcomes.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, AiProviderFailureKind.ModelUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, AiProviderFailureKind.UnsupportedResponse)]
    [InlineData(HttpStatusCode.UnprocessableEntity, AiProviderFailureKind.UnsupportedResponse)]
    [InlineData(HttpStatusCode.InternalServerError, AiProviderFailureKind.HttpFailure)]
    public async Task GenerateAsync_HttpFailure_MapsControlledFailure(HttpStatusCode statusCode, AiProviderFailureKind expected)
    {
        using var client = new HttpClient(new StubHandler(_ => Json(statusCode, "{}")));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GenerateAsync(new AiProviderGenerationRequest(
            AiSuggestionKind.FileRename,
            "http://127.0.0.1:11434",
            "model",
            "prompt",
            TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds)), CancellationToken.None);

        Assert.Equal(expected, result.FailureKind);
        Assert.Null(result.StructuredJson);
    }

    /// <summary>Verifies an unknown internal capability value is blocked before transport.</summary>
    [Fact]
    public async Task GenerateAsync_UnknownSuggestionKind_ReturnsConfigurationFailureWithoutRequest()
    {
        var requestCount = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requestCount++;
            return Json(HttpStatusCode.OK, "{}");
        }));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GenerateAsync(new AiProviderGenerationRequest(
            (AiSuggestionKind)999,
            "http://127.0.0.1:11434",
            "model",
            "prompt",
            TimeSpan.FromSeconds(1)), CancellationToken.None);

        Assert.Equal(AiProviderFailureKind.Configuration, result.FailureKind);
        Assert.Equal(0, requestCount);
    }

    /// <summary>Verifies invalid model identifiers fail before any provider request is sent.</summary>
    [Fact]
    public async Task GenerateAsync_ControlCharacterModel_ReturnsConfigurationFailureWithoutRequest()
    {
        var requestCount = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requestCount++;
            return Json(HttpStatusCode.OK, "{}");
        }));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GenerateAsync(new AiProviderGenerationRequest(
            AiSuggestionKind.FileRename,
            "http://127.0.0.1:11434",
            "bad\nmodel",
            "prompt",
            TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds)), CancellationToken.None);

        Assert.Equal(AiProviderFailureKind.Configuration, result.FailureKind);
        Assert.Equal(0, requestCount);
    }

    /// <summary>Verifies an oversized prompt is rejected before transport.</summary>
    [Fact]
    public async Task GenerateAsync_OversizedPrompt_ReturnsConfigurationFailureWithoutRequest()
    {
        var requestCount = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requestCount++;
            return Json(HttpStatusCode.OK, "{}");
        }));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GenerateAsync(new AiProviderGenerationRequest(
            AiSuggestionKind.FileRename,
            "http://127.0.0.1:11434",
            "model",
            new string('p', OllamaTransportLimits.MaximumPromptBytes + 1),
            TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds)), CancellationToken.None);

        Assert.Equal(AiProviderFailureKind.Configuration, result.FailureKind);
        Assert.Equal(0, requestCount);
    }

    /// <summary>Verifies an oversized generation response is rejected as untrusted provider data.</summary>
    [Fact]
    public async Task GenerateAsync_OversizedResponse_ReturnsInvalidResponse()
    {
        var payload = new string(' ', OllamaTransportLimits.MaximumResponseBytes + 1);
        using var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, payload)));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());

        var result = await provider.GenerateAsync(new AiProviderGenerationRequest(
            AiSuggestionKind.FileRename,
            "http://127.0.0.1:11434",
            "model",
            "prompt",
            TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds)), CancellationToken.None);

        Assert.Equal(AiProviderFailureKind.InvalidResponse, result.FailureKind);
        Assert.Null(result.StructuredJson);
    }

    /// <summary>
    /// Verifies request timeout and cancellation are distinct safe outcomes.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_TimeoutAndCancellation_ReturnDistinctFailures()
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
        {
            return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
        }));
        var provider = new OllamaSuggestionProvider(client, new LoggingService());
        var request = new AiProviderGenerationRequest(
            AiSuggestionKind.FileRename,
            "http://127.0.0.1:11434",
            "model",
            "prompt",
            TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds));

        var timeout = await provider.GenerateAsync(request, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await provider.GenerateAsync(request, cancellation.Token);

        Assert.Equal(AiProviderFailureKind.Timeout, timeout.FailureKind);
        Assert.Equal(AiProviderFailureKind.Cancelled, cancelled.FailureKind);
    }

    private static AiSettings EnabledSettings() => new() { Enabled = true };

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
