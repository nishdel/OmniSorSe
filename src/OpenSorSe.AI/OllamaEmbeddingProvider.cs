using System.Net.Http.Headers;
using System.Text.Json;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.AI;

/// <summary>Creates bounded learned vectors with a separately selected, installed local Ollama model.</summary>
public sealed class OllamaEmbeddingProvider : IModelEmbeddingProvider
{
    /// <summary>Gets the maximum number of text chunks in one request.</summary>
    public const int MaximumBatchSize = 16;

    /// <summary>Gets the maximum UTF-16 text length for one chunk.</summary>
    public const int MaximumInputCharacters = 1600;

    /// <summary>Gets the largest supported learned vector dimension.</summary>
    public const int MaximumDimensions = 4096;

    private readonly HttpClient _httpClient;
    private readonly IConfigurationService _configuration;
    private CachedModel? _cachedModel;

    /// <summary>Uses a caller-owned transport configured with automatic redirects disabled.</summary>
    public OllamaEmbeddingProvider(HttpClient httpClient, IConfigurationService configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task<SemanticModelIdentity?> GetModelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = CaptureSettings();
        if (settings is null)
        {
            return null;
        }

        using var timeout = CreateTimeout(settings, cancellationToken);
        try
        {
            var installed = await GetInstalledModelAsync(settings, timeout.Token).ConfigureAwait(false);
            if (installed is null)
            {
                return null;
            }

            // A tag is mutable. Recheck its digest even when dimensions have already been discovered.
            var cached = Volatile.Read(ref _cachedModel);
            if (cached is not null && cached.Endpoint == settings.Endpoint &&
                Matches(cached.Identity, installed) && CaptureSettings() == settings)
            {
                return cached.Identity;
            }

            var result = await EmbedCoreAsync(settings, installed, ["embedding dimension probe"], null, timeout.Token)
                .ConfigureAwait(false);
            return result.Model;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ModelEmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs,
        SemanticModelIdentity? expectedModel = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (inputs.Count is < 1 or > MaximumBatchSize ||
            inputs.Any(text => string.IsNullOrWhiteSpace(text) || text.Length > MaximumInputCharacters))
        {
            throw new ArgumentException("Embedding inputs must contain 1–16 nonempty chunks of at most 1600 characters.", nameof(inputs));
        }

        var settings = CaptureSettings();
        if (settings is null)
        {
            return Unavailable("Local embeddings are disabled or their local endpoint is invalid.");
        }

        // Snapshot caller-owned collections before crossing an asynchronous boundary.
        var snapshot = inputs.ToArray();
        using var timeout = CreateTimeout(settings, cancellationToken);
        try
        {
            var installed = await GetInstalledModelAsync(settings, timeout.Token).ConfigureAwait(false);
            if (installed is null)
            {
                return Unavailable("The selected embedding model is not installed or does not expose a model digest.");
            }

            if (expectedModel is not null && !Matches(expectedModel, installed))
            {
                return Unavailable("The embedding model changed; compatible vectors must be rebuilt.");
            }

            return await EmbedCoreAsync(settings, installed, snapshot, expectedModel, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("The local embedding request timed out.");
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return Unavailable("The local embedding model is unavailable or returned invalid vectors.");
        }
    }

    private async Task<ModelEmbeddingResult> EmbedCoreAsync(SettingsSnapshot settings, InstalledModel installed,
        IReadOnlyList<string> inputs, SemanticModelIdentity? expectedModel, CancellationToken cancellationToken)
    {
        if (CaptureSettings() != settings)
        {
            return Unavailable("Embedding settings changed during the request.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { model = installed.Name, input = inputs, truncate = false });
        if (payload.Length > OllamaTransportLimits.MaximumPromptBytes)
        {
            return Unavailable("The embedding request exceeds its encoded byte bound.", isDocumentFailure: true);
        }

        IReadOnlyList<IReadOnlyList<float>> vectors;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.Endpoint, "api/embed"))
            {
                Content = new ByteArrayContent(payload),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Embedding response did not identify the requested model.");
            }

            if (!string.Equals(model.GetString(), installed.Name, StringComparison.Ordinal))
            {
                return Unavailable("The embedding response identified a different model; compatible vectors must be rebuilt.");
            }

            vectors = ReadVectors(json.RootElement, inputs.Count, expectedModel?.Dimensions);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException ||
            exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity })
        {
            // Only the input/embedding boundary is document-specific. A broken model catalog,
            // connectivity failure, or changed model must not exhaust every file's retry budget.
            return Unavailable("The local model could not produce valid vectors for this document batch.", isDocumentFailure: true);
        }

        var current = await GetInstalledModelAsync(settings, cancellationToken).ConfigureAwait(false);
        if (current != installed || CaptureSettings() != settings)
        {
            return Unavailable("The embedding model or settings changed during the request.");
        }

        var identity = new SemanticModelIdentity("ollama", installed.Name, installed.Digest, vectors[0].Count);
        Volatile.Write(ref _cachedModel, new CachedModel(settings.Endpoint, identity));
        return new ModelEmbeddingResult(identity, vectors);
    }

    private async Task<InstalledModel?> GetInstalledModelAsync(SettingsSnapshot settings, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(settings.Endpoint, "api/tags"));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false));
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Invalid installed embedding model list.");
        }

        // Ollama resolves an omitted tag to :latest; retain the canonical installed name as identity.
        var selected = settings.Model.LastIndexOf(':') > settings.Model.LastIndexOf('/') ? settings.Model : settings.Model + ":latest";
        foreach (var candidate in models.EnumerateArray().Take(1000))
        {
            if (candidate.ValueKind == JsonValueKind.Object &&
                candidate.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() is { } modelName &&
                (string.Equals(modelName, selected, StringComparison.Ordinal) || string.Equals(modelName, settings.Model, StringComparison.Ordinal)) &&
                candidate.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String &&
                digest.GetString() is { Length: > 0 and <= 256 } modelDigest &&
                !string.IsNullOrWhiteSpace(modelDigest) && !modelDigest.Any(char.IsControl))
            {
                return new InstalledModel(modelName, modelDigest);
            }
        }

        return null;
    }

    private static IReadOnlyList<IReadOnlyList<float>> ReadVectors(JsonElement root, int count, int? expectedDimensions)
    {
        if (!root.TryGetProperty("embeddings", out var embeddings) || embeddings.ValueKind != JsonValueKind.Array ||
            embeddings.GetArrayLength() != count)
        {
            throw new InvalidDataException("Embedding count does not match its inputs.");
        }

        var vectors = new List<IReadOnlyList<float>>(count);
        var dimensions = expectedDimensions;
        foreach (var embedding in embeddings.EnumerateArray())
        {
            if (embedding.ValueKind != JsonValueKind.Array || embedding.GetArrayLength() is < 1 or > MaximumDimensions ||
                dimensions.HasValue && embedding.GetArrayLength() != dimensions.Value)
            {
                throw new InvalidDataException("Embedding dimensions are invalid or inconsistent.");
            }

            dimensions ??= embedding.GetArrayLength();
            var vector = new float[dimensions.Value];
            var index = 0;
            double squaredNorm = 0;
            foreach (var element in embedding.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out var value) || !float.IsFinite(value))
                {
                    throw new InvalidDataException("Embedding contains a non-finite or nonnumeric value.");
                }

                vector[index++] = value;
                squaredNorm += (double)value * value;
            }

            if (squaredNorm <= 0 || !double.IsFinite(squaredNorm))
            {
                throw new InvalidDataException("Embedding has no finite direction.");
            }

            var norm = Math.Sqrt(squaredNorm);
            for (var dimension = 0; dimension < vector.Length; dimension++)
            {
                vector[dimension] = (float)(vector[dimension] / norm);
            }

            vectors.Add(vector);
        }

        return vectors;
    }

    private SettingsSnapshot? CaptureSettings()
    {
        var settings = _configuration.Current;
        var model = settings.SemanticSearch.EmbeddingModel?.Trim();
        return settings.SemanticSearch.Enabled && settings.SemanticSearch.EmbeddingsEnabled &&
            !string.IsNullOrWhiteSpace(model) && model.Length <= AiSettings.MaximumModelIdentifierLength && !model.Any(char.IsControl) &&
            OllamaEndpointNormalizer.TryNormalize(settings.Ai.Endpoint, out var endpoint) && endpoint.IsLoopback
            ? new SettingsSnapshot(endpoint, model, Math.Clamp(settings.Ai.RequestTimeoutSeconds,
                AiSettings.MinimumRequestTimeoutSeconds, AiSettings.MaximumRequestTimeoutSeconds))
            : null;
    }

    private static CancellationTokenSource CreateTimeout(SettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        return timeout;
    }

    private static bool Matches(SemanticModelIdentity identity, InstalledModel installed) =>
        identity.Provider == "ollama" && identity.Model == installed.Name && identity.Revision == installed.Digest;

    private static bool IsProviderFailure(Exception exception) => exception is HttpRequestException or JsonException or IOException or InvalidDataException;

    private static ModelEmbeddingResult Unavailable(string reason, bool isDocumentFailure = false) =>
        new(null, [], reason) { IsDocumentFailure = isDocumentFailure };

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > OllamaTransportLimits.MaximumResponseBytes)
        {
            throw new InvalidDataException("Embedding response exceeds its byte bound.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > OllamaTransportLimits.MaximumResponseBytes)
            {
                throw new InvalidDataException("Embedding response exceeds its byte bound.");
            }

            result.Write(buffer, 0, count);
        }

        return result.ToArray();
    }

    private sealed record SettingsSnapshot(Uri Endpoint, string Model, int TimeoutSeconds);
    private sealed record InstalledModel(string Name, string Digest);
    private sealed record CachedModel(Uri Endpoint, SemanticModelIdentity Identity);
}
