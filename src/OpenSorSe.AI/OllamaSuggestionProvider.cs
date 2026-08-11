using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.AI;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.AI;

/// <summary>Implements the provider-neutral suggestion boundary against Ollama.</summary>
public sealed class OllamaSuggestionProvider : IAiSuggestionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly IAiDiagnosticsCollector? _diagnostics;

    /// <summary>Initializes the transport with one caller-owned reusable HTTP client.</summary>
    public OllamaSuggestionProvider(
        HttpClient httpClient,
        ILoggingService loggingService,
        IAiDiagnosticsCollector? diagnostics = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService))).CreateLogger(nameof(OllamaSuggestionProvider));
        _diagnostics = AiDiagnosticsIsolation.Protect(diagnostics);
    }

    /// <inheritdoc />
    public Task<AiConnectionResult> CheckConnectionAsync(AiSettings settings, CancellationToken cancellationToken) =>
        CheckConnectionAsync(settings, null, cancellationToken);

    /// <inheritdoc />
    public async Task<AiConnectionResult> CheckConnectionAsync(AiSettings settings, string? diagnosticRequestId, CancellationToken cancellationToken)
    {
        var validation = ValidateConnectionSettings(settings);
        if (validation is not null)
        {
            return validation;
        }

        OllamaEndpointNormalizer.TryNormalize(settings.Endpoint, out var endpoint);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var requestCancellation = CreateTimeoutCancellation(settings.RequestTimeoutSeconds, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "api/version"));
            _diagnostics?.Capture(diagnosticRequestId, AiDiagnosticContentKind.RequestJson, """{"method":"GET","path":"/api/version","body":null}""");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token).ConfigureAwait(false);
            _diagnostics?.ReportStage(diagnosticRequestId, "Request sent", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.ReportStage(diagnosticRequestId, "Response headers received", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (!response.IsSuccessStatusCode)
            {
                var failureBytes = await ReadBoundedContentAsync(response.Content, requestCancellation.Token).ConfigureAwait(false);
                _diagnostics?.Capture(diagnosticRequestId, AiDiagnosticContentKind.RawHttpResponse, Encoding.UTF8.GetString(failureBytes));
                _diagnostics?.SetTransport(diagnosticRequestId, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), SafeHeaders(response), failureBytes.Length, true, false);
                _logger.LogWarning("Ollama connection check returned HTTP {StatusCode} after {ElapsedMilliseconds} ms.", (int)response.StatusCode, (long)elapsed.TotalMilliseconds);
                return ConnectionFailure("Ollama is reachable but did not accept the connection check.", endpoint, elapsed, response.StatusCode);
            }

            var responseBytes = await ReadBoundedContentAsync(response.Content, requestCancellation.Token).ConfigureAwait(false);
            var rawResponse = Encoding.UTF8.GetString(responseBytes);
            _diagnostics?.Capture(diagnosticRequestId, AiDiagnosticContentKind.RawHttpResponse, rawResponse);
            _diagnostics?.ReportStage(diagnosticRequestId, "Response body received", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.SetTransport(diagnosticRequestId, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), SafeHeaders(response), responseBytes.Length, true, false);
            var payload = JsonSerializer.Deserialize<VersionResponse>(responseBytes, JsonOptions);
            var version = string.IsNullOrWhiteSpace(payload?.Version) ? null : Bound(payload.Version, 128);
            _logger.LogInformation("Ollama connection check succeeded after {ElapsedMilliseconds} ms.", (long)elapsed.TotalMilliseconds);
            return new AiConnectionResult(
                AiAvailabilityState.Connected,
                version is null ? "Ollama is reachable." : $"Ollama is reachable (version {version}).",
                Array.Empty<AiModel>())
            {
                NormalizedEndpoint = endpoint.AbsoluteUri.TrimEnd('/'),
                ProviderVersion = version,
                HttpStatusCode = (int)response.StatusCode,
                Elapsed = elapsed,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ConnectionFailure("The Ollama connection check was cancelled.", endpoint, Stopwatch.GetElapsedTime(started), null, AiAvailabilityState.RequestCancelled);
        }
        catch (OperationCanceledException)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning("Ollama connection check timed out after {ElapsedMilliseconds} ms.", (long)elapsed.TotalMilliseconds);
            return ConnectionFailure($"The Ollama connection check timed out after {settings.RequestTimeoutSeconds} seconds.", endpoint, elapsed);
        }
        catch (HttpRequestException exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning(exception, "Ollama connection check could not reach the configured endpoint after {ElapsedMilliseconds} ms.", (long)elapsed.TotalMilliseconds);
            return ConnectionFailure("Ollama is unavailable at the configured endpoint. Confirm that Ollama is installed and running.", endpoint, elapsed);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning(exception, "Ollama connection check returned invalid bounded data after {ElapsedMilliseconds} ms.", (long)elapsed.TotalMilliseconds);
            return ConnectionFailure("Ollama returned invalid connection information.", endpoint, elapsed);
        }
    }

    /// <inheritdoc />
    public Task<AiConnectionResult> GetConnectionAsync(AiSettings settings, CancellationToken cancellationToken) =>
        GetConnectionAsync(settings, null, cancellationToken);

    /// <inheritdoc />
    public async Task<AiConnectionResult> GetConnectionAsync(AiSettings settings, string? diagnosticRequestId, CancellationToken cancellationToken)
    {
        var validation = ValidateConnectionSettings(settings);
        if (validation is not null)
        {
            return validation;
        }

        OllamaEndpointNormalizer.TryNormalize(settings.Endpoint, out var endpoint);
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var requestCancellation = CreateTimeoutCancellation(settings.RequestTimeoutSeconds, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "api/tags"));
            _diagnostics?.Capture(diagnosticRequestId, AiDiagnosticContentKind.RequestJson, """{"method":"GET","path":"/api/tags","body":null}""");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token).ConfigureAwait(false);
            _diagnostics?.ReportStage(diagnosticRequestId, "Request sent", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.ReportStage(diagnosticRequestId, "Response headers received", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (!response.IsSuccessStatusCode)
            {
                var failureBytes = await ReadBoundedContentAsync(response.Content, requestCancellation.Token).ConfigureAwait(false);
                _diagnostics?.Capture(diagnosticRequestId, AiDiagnosticContentKind.RawHttpResponse, Encoding.UTF8.GetString(failureBytes));
                _diagnostics?.SetTransport(diagnosticRequestId, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), SafeHeaders(response), failureBytes.Length, true, false);
                _logger.LogWarning("Ollama model discovery returned HTTP {StatusCode} after {ElapsedMilliseconds} ms.", (int)response.StatusCode, (long)elapsed.TotalMilliseconds);
                return ConnectionFailure("Ollama did not accept model discovery.", endpoint, elapsed, response.StatusCode);
            }

            var responseBytes = await ReadBoundedContentAsync(response.Content, requestCancellation.Token).ConfigureAwait(false);
            var rawResponse = Encoding.UTF8.GetString(responseBytes);
            _diagnostics?.Capture(diagnosticRequestId, AiDiagnosticContentKind.RawHttpResponse, rawResponse);
            _diagnostics?.ReportStage(diagnosticRequestId, "Response body received", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.SetTransport(diagnosticRequestId, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), SafeHeaders(response), responseBytes.Length, true, false);
            var payload = JsonSerializer.Deserialize<ModelsResponse>(responseBytes, JsonOptions);
            if (payload?.Models is null)
            {
                return ConnectionFailure("Ollama returned an invalid model list.", endpoint, elapsed, response.StatusCode);
            }

            var runtime = await GetRunningModelsAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var models = payload.Models
                .Where(model => IsValidModelIdentifier(model.Name))
                .Select(model => new AiModel(model.Name!, model.Name!)
                {
                    RuntimeState = runtime.ModelIds.Contains(model.Name!)
                        ? AiModelRuntimeState.Running
                        : AiModelRuntimeState.Available,
                })
                .DistinctBy(model => model.Id, StringComparer.Ordinal)
                .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(OllamaTransportLimits.MaximumPublishedModelCount)
                .ToArray();
            var runningCount = models.Count(model => model.RuntimeState == AiModelRuntimeState.Running);
            var completedElapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogInformation("Ollama model discovery found {ModelCount} model(s) after {ElapsedMilliseconds} ms.", models.Length, (long)completedElapsed.TotalMilliseconds);
            return new AiConnectionResult(
                models.Length == 0 ? AiAvailabilityState.NoModelsAvailable : AiAvailabilityState.Connected,
                models.Length == 0
                    ? "Ollama is reachable, but no installed models were found."
                    : runtime.IsAvailable
                        ? $"Discovered {models.Length} installed Ollama model(s); {runningCount} currently running."
                        : $"Discovered {models.Length} installed Ollama model(s). Current runtime state could not be checked.",
                Array.AsReadOnly(models))
            {
                NormalizedEndpoint = endpoint.AbsoluteUri.TrimEnd('/'),
                HttpStatusCode = (int)response.StatusCode,
                Elapsed = completedElapsed,
                RuntimeStateAvailable = runtime.IsAvailable,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ConnectionFailure("Ollama model discovery was cancelled.", endpoint, Stopwatch.GetElapsedTime(started), null, AiAvailabilityState.RequestCancelled);
        }
        catch (OperationCanceledException)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            return ConnectionFailure($"Ollama model discovery timed out after {settings.RequestTimeoutSeconds} seconds.", endpoint, elapsed);
        }
        catch (HttpRequestException exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning(exception, "Ollama model discovery could not reach the configured endpoint after {ElapsedMilliseconds} ms.", (long)elapsed.TotalMilliseconds);
            return ConnectionFailure("Ollama is unavailable at the configured endpoint. Confirm that Ollama is installed and running.", endpoint, elapsed);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning(exception, "Ollama model discovery returned invalid bounded data after {ElapsedMilliseconds} ms.", (long)elapsed.TotalMilliseconds);
            return ConnectionFailure(exception is InvalidDataException ? "Ollama returned an oversized model list." : "Ollama returned an invalid model list.", endpoint, elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<AiProviderGenerationResult> GenerateAsync(AiProviderGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind is not (AiSuggestionKind.FileRename or AiSuggestionKind.FolderStructure or AiSuggestionKind.DocumentTextInterpretation or AiSuggestionKind.SearchReranking) ||
            !OllamaEndpointNormalizer.TryNormalize(request.Endpoint, out var endpoint) ||
            !IsValidModelIdentifier(request.Model) ||
            string.IsNullOrWhiteSpace(request.Prompt) ||
            Encoding.UTF8.GetByteCount(request.Prompt) > OllamaTransportLimits.MaximumPromptBytes ||
            request.Timeout < TimeSpan.FromSeconds(AiSettings.MinimumRequestTimeoutSeconds) ||
            request.Timeout > TimeSpan.FromSeconds(AiSettings.MaximumRequestTimeoutSeconds))
        {
            return new AiProviderGenerationResult(null, AiProviderFailureKind.Configuration, "The Ollama suggestion settings are incomplete or outside supported limits.");
        }

        var started = Stopwatch.GetTimestamp();
        var normalizedEndpoint = endpoint.AbsoluteUri.TrimEnd('/');
        var serializedRequest = string.Empty;
        var receivedEnvelope = string.Empty;
        try
        {
            using var requestCancellation = CreateTimeoutCancellation(request.Timeout, cancellationToken);
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Serializing Ollama request", AiDiagnosticState.Active, Stopwatch.GetElapsedTime(started));
            var payload = new GenerateRequest(
                request.Model,
                request.SystemPrompt,
                request.UserPrompt,
                false,
                AiStructuredOutputContracts.GetSchema(request.Kind),
                "5m",
                new GenerateOptions(0.0));
            serializedRequest = JsonSerializer.Serialize(payload);
            _diagnostics?.Capture(request.DiagnosticRequestId, AiDiagnosticContentKind.SystemPrompt, request.SystemPrompt);
            _diagnostics?.Capture(request.DiagnosticRequestId, AiDiagnosticContentKind.UserPrompt, request.UserPrompt);
            _diagnostics?.Capture(request.DiagnosticRequestId, AiDiagnosticContentKind.RequestJson, serializedRequest);
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Serializing Ollama request", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "api/generate"))
            {
                Content = new StringContent(serializedRequest, Encoding.UTF8, "application/json"),
            };
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Connecting to Ollama", AiDiagnosticState.Active, Stopwatch.GetElapsedTime(started));
            Report(request, AiRequestStage.SendingRequest, "Sending the bounded request to Ollama.", started);
            var sendTask = _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token);
            Report(request, AiRequestStage.WaitingForModel, "Waiting for the selected model to respond.", started);
            using var response = await sendTask.ConfigureAwait(false);
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Request sent", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Response headers received", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.SetTransport(
                request.DiagnosticRequestId,
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                SafeHeaders(response),
                0,
                false,
                false);
            Report(request, AiRequestStage.ReceivingResponse, "Receiving the bounded Ollama response.", started);
            var responseBytes = await ReadBoundedContentAsync(response.Content, requestCancellation.Token).ConfigureAwait(false);
            receivedEnvelope = Encoding.UTF8.GetString(responseBytes);
            _diagnostics?.Capture(request.DiagnosticRequestId, AiDiagnosticContentKind.RawHttpResponse, receivedEnvelope);
            _diagnostics?.SetTransport(
                request.DiagnosticRequestId,
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                SafeHeaders(response),
                responseBytes.Length,
                true,
                false);
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Response body received", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (!response.IsSuccessStatusCode)
            {
                var failureKind = MapHttpFailure(response.StatusCode, receivedEnvelope);
                _logger.LogWarning("Ollama {SuggestionKind} request for model {Model} returned HTTP {StatusCode} after {ElapsedMilliseconds} ms.", request.Kind, request.Model, (int)response.StatusCode, (long)elapsed.TotalMilliseconds);
                return WithDiagnostics(
                    new AiProviderGenerationResult(null, failureKind, FailureMessage(failureKind, response.StatusCode)),
                    normalizedEndpoint,
                    response.StatusCode,
                    elapsed,
                    receivedEnvelope,
                    string.Empty,
                    serializedRequest,
                    response.Content.Headers.ContentType?.ToString());
            }

            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Extracting assistant content", AiDiagnosticState.Active, Stopwatch.GetElapsedTime(started));
            var payloadResponse = JsonSerializer.Deserialize<GenerateResponse>(responseBytes, JsonOptions);
            if (string.IsNullOrWhiteSpace(payloadResponse?.Response))
            {
                return WithDiagnostics(
                    new AiProviderGenerationResult(null, AiProviderFailureKind.InvalidResponse, "Ollama returned no structured suggestion data."),
                    normalizedEndpoint,
                    response.StatusCode,
                    elapsed,
                    receivedEnvelope,
                    payloadResponse?.Response ?? string.Empty,
                    serializedRequest,
                    response.Content.Headers.ContentType?.ToString());
            }

            _diagnostics?.Capture(request.DiagnosticRequestId, AiDiagnosticContentKind.ExtractedAssistantResponse, payloadResponse.Response);
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Extracting assistant content", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Preserving exact assistant content", AiDiagnosticState.Succeeded, Stopwatch.GetElapsedTime(started));
            _diagnostics?.ReportStage(request.DiagnosticRequestId, "Parsing structured JSON", AiDiagnosticState.Active, Stopwatch.GetElapsedTime(started));
            _logger.LogInformation("Ollama {SuggestionKind} request for model {Model} succeeded after {ElapsedMilliseconds} ms.", request.Kind, request.Model, (long)elapsed.TotalMilliseconds);
            return WithDiagnostics(
                new AiProviderGenerationResult(payloadResponse.Response, AiProviderFailureKind.None, "Ollama returned a response for validation."),
                normalizedEndpoint,
                response.StatusCode,
                elapsed,
                receivedEnvelope,
                payloadResponse.Response,
                serializedRequest,
                response.Content.Headers.ContentType?.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            return WithDiagnostics(
                new AiProviderGenerationResult(null, AiProviderFailureKind.Cancelled, "The suggestion request was cancelled."),
                normalizedEndpoint,
                null,
                elapsed,
                receivedEnvelope,
                requestJson: serializedRequest);
        }
        catch (OperationCanceledException)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning("Ollama {SuggestionKind} request for model {Model} timed out after {ElapsedMilliseconds} ms.", request.Kind, request.Model, (long)elapsed.TotalMilliseconds);
            return WithDiagnostics(
                new AiProviderGenerationResult(null, AiProviderFailureKind.Timeout, $"The Ollama suggestion request timed out after {(int)request.Timeout.TotalSeconds} seconds."),
                normalizedEndpoint,
                null,
                elapsed,
                receivedEnvelope,
                requestJson: serializedRequest);
        }
        catch (HttpRequestException exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning(exception, "Ollama {SuggestionKind} request for model {Model} could not reach the configured endpoint after {ElapsedMilliseconds} ms.", request.Kind, request.Model, (long)elapsed.TotalMilliseconds);
            return WithDiagnostics(
                new AiProviderGenerationResult(null, AiProviderFailureKind.Unavailable, "Ollama is unavailable at the configured endpoint. Confirm that Ollama is running."),
                normalizedEndpoint,
                null,
                elapsed,
                receivedEnvelope,
                requestJson: serializedRequest);
        }
        catch (JsonException exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning(exception, "Ollama {SuggestionKind} response for model {Model} was malformed after {ElapsedMilliseconds} ms.", request.Kind, request.Model, (long)elapsed.TotalMilliseconds);
            return WithDiagnostics(
                new AiProviderGenerationResult(null, AiProviderFailureKind.InvalidResponse, "Ollama returned malformed response data."),
                normalizedEndpoint,
                null,
                elapsed,
                receivedEnvelope,
                requestJson: serializedRequest);
        }
        catch (InvalidDataException exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            _logger.LogWarning(exception, "Ollama {SuggestionKind} response for model {Model} exceeded the supported size after {ElapsedMilliseconds} ms.", request.Kind, request.Model, (long)elapsed.TotalMilliseconds);
            return WithDiagnostics(
                new AiProviderGenerationResult(null, AiProviderFailureKind.InvalidResponse, "Ollama returned an oversized response."),
                normalizedEndpoint,
                null,
                elapsed,
                receivedEnvelope,
                requestJson: serializedRequest);
        }
    }

    private static AiConnectionResult? ValidateConnectionSettings(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            return new AiConnectionResult(AiAvailabilityState.Disabled, "AI assistance is disabled in Settings.", Array.Empty<AiModel>());
        }

        return !OllamaEndpointNormalizer.TryNormalize(settings.Endpoint, out _) ||
               settings.RequestTimeoutSeconds is < AiSettings.MinimumRequestTimeoutSeconds or > AiSettings.MaximumRequestTimeoutSeconds
            ? new AiConnectionResult(AiAvailabilityState.Unavailable, "The Ollama endpoint or request timeout is invalid.", Array.Empty<AiModel>())
            : null;
    }

    private static AiConnectionResult ConnectionFailure(
        string message,
        Uri endpoint,
        TimeSpan elapsed,
        HttpStatusCode? statusCode = null,
        AiAvailabilityState state = AiAvailabilityState.Unavailable) =>
        new(state, message, Array.Empty<AiModel>())
        {
            NormalizedEndpoint = endpoint.AbsoluteUri.TrimEnd('/'),
            HttpStatusCode = statusCode is null ? null : (int)statusCode,
            Elapsed = elapsed,
        };

    private static AiProviderGenerationResult WithDiagnostics(
        AiProviderGenerationResult result,
        string endpoint,
        HttpStatusCode? statusCode,
        TimeSpan elapsed,
        string rawResponse,
        string extractedResponse = "",
        string requestJson = "",
        string? contentType = null) => result with
        {
            Diagnostics = new AiProviderRequestDiagnostics(
                endpoint,
                statusCode is null ? null : (int)statusCode,
                elapsed,
                rawResponse.Length,
                Encoding.UTF8.GetByteCount(rawResponse),
                rawResponse)
            {
                RawHttpResponse = rawResponse,
                ExtractedAssistantResponse = extractedResponse,
                RequestJson = requestJson,
                ContentType = contentType ?? string.Empty,
            },
        };

    private static AiProviderFailureKind MapHttpFailure(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode == HttpStatusCode.NotFound || responseBody.Contains("model", StringComparison.OrdinalIgnoreCase) && responseBody.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return AiProviderFailureKind.ModelUnavailable;
        }

        return statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
            ? AiProviderFailureKind.UnsupportedResponse
            : AiProviderFailureKind.HttpFailure;
    }

    private static string FailureMessage(AiProviderFailureKind failureKind, HttpStatusCode statusCode) => failureKind switch
    {
        AiProviderFailureKind.ModelUnavailable => "The exact configured Ollama model is unavailable. Refresh installed models and select one.",
        AiProviderFailureKind.UnsupportedResponse => "Ollama or the selected model rejected the required JSON response format. Try another installed model.",
        _ => $"Ollama could not complete the request (HTTP {(int)statusCode}).",
    };

    private static bool IsValidModelIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= AiSettings.MaximumModelIdentifierLength &&
        !value.Any(char.IsControl);

    private async Task<RunningModelsResult> GetRunningModelsAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runtimeCancellation.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "api/ps"));
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, runtimeCancellation.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return RunningModelsResult.Unavailable;
            }

            var responseBytes = await ReadBoundedContentAsync(response.Content, runtimeCancellation.Token)
                .ConfigureAwait(false);
            var payload = JsonSerializer.Deserialize<ModelsResponse>(responseBytes, JsonOptions);
            if (payload?.Models is null)
            {
                return RunningModelsResult.Unavailable;
            }

            return new RunningModelsResult(
                true,
                payload.Models
                    .Where(model => IsValidModelIdentifier(model.Name))
                    .Select(model => model.Name!)
                    .Take(OllamaTransportLimits.MaximumPublishedModelCount)
                    .ToHashSet(StringComparer.Ordinal));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            OperationCanceledException or
            HttpRequestException or
            JsonException or
            InvalidDataException)
        {
            _logger.LogDebug(exception, "Ollama running-model status was unavailable; installed-model discovery remains usable.");
            return RunningModelsResult.Unavailable;
        }
    }

    private static async Task<byte[]> ReadBoundedContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > OllamaTransportLimits.MaximumResponseBytes)
        {
            throw new InvalidDataException("The provider response exceeds the supported size.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > OllamaTransportLimits.MaximumResponseBytes)
            {
                throw new InvalidDataException("The provider response exceeds the supported size.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static CancellationTokenSource CreateTimeoutCancellation(int seconds, CancellationToken cancellationToken) =>
        CreateTimeoutCancellation(TimeSpan.FromSeconds(seconds), cancellationToken);

    private static CancellationTokenSource CreateTimeoutCancellation(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        return linked;
    }

    private static void Report(AiProviderGenerationRequest request, AiRequestStage stage, string message, long started) =>
        request.Progress?.Report(new AiRequestProgress(stage, message, Stopwatch.GetElapsedTime(started)));

    private static string Bound(string value, int maximumLength) => value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record ModelsResponse([property: JsonPropertyName("models")] List<ModelResponse>? Models);
    private sealed record ModelResponse([property: JsonPropertyName("name")] string? Name);
    private sealed record VersionResponse([property: JsonPropertyName("version")] string? Version);

    private sealed record GenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] JsonElement Format,
        [property: JsonPropertyName("keep_alive")] string KeepAlive,
        [property: JsonPropertyName("options")] GenerateOptions Options);

    private sealed record GenerateOptions([property: JsonPropertyName("temperature")] double Temperature);
    private sealed record GenerateResponse([property: JsonPropertyName("response")] string? Response);

    private sealed record RunningModelsResult(bool IsAvailable, IReadOnlySet<string> ModelIds)
    {
        public static RunningModelsResult Unavailable { get; } = new(false, new HashSet<string>(StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> SafeHeaders(HttpResponseMessage response)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            if (header.Key.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("ETag", StringComparison.OrdinalIgnoreCase))
            {
                values[header.Key] = string.Join(", ", header.Value);
            }
        }
        return values;
    }
}
