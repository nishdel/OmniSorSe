using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Semantic;

/// <summary>Builds a bounded incremental local semantic index from extracted content records.</summary>
public sealed class SemanticIndexer : ISemanticIndexer
{
    private readonly IConfigurationService _configurationService;
    private readonly IContentStore _contentStore;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ISemanticIndexStore _indexStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the local incremental indexer.</summary>
    public SemanticIndexer(
        IConfigurationService configurationService,
        IContentStore contentStore,
        IEmbeddingProvider embeddingProvider,
        ISemanticIndexStore indexStore,
        TimeProvider? timeProvider = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<SemanticResult<int>> BuildAsync(
        bool rebuild,
        IProgress<SemanticIndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var settings = _configurationService.Current.SemanticSearch;
        if (!settings.Enabled)
        {
            return new SemanticResult<int>(
                SemanticState.Disabled,
                "Semantic Search Beta is disabled in Settings.",
                0);
        }

        try
        {
            var content = (await _contentStore.ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(record => File.Exists(record.FullPath))
                .OrderBy(record => record.FullPath, PathComparer)
                .Take(settings.MaximumDocumentCount)
                .ToArray();
            await _contentStore.RemoveMissingAsync(
                Array.AsReadOnly(content.Select(record => record.FullPath).ToArray()),
                cancellationToken).ConfigureAwait(false);
            var existing = rebuild
                ? new Dictionary<string, SemanticIndexEntry>(PathComparer)
                : (await _indexStore.ListAsync(cancellationToken).ConfigureAwait(false))
                    .ToDictionary(entry => entry.FullPath, PathComparer);
            var entries = new List<SemanticIndexEntry>(content.Length);
            for (var index = 0; index < content.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = content[index];
                var fingerprint = CreateIndexFingerprint(record);
                if (existing.TryGetValue(record.FullPath, out var cached) &&
                    string.Equals(cached.IndexFingerprint, fingerprint, StringComparison.Ordinal) &&
                    cached.Vector.Count == _embeddingProvider.Dimensions)
                {
                    entries.Add(cached);
                }
                else
                {
                    entries.Add(CreateEntry(record, fingerprint));
                }

                progress?.Report(new SemanticIndexProgress(
                    index + 1,
                    content.Length,
                    $"Indexed {index + 1} of {content.Length} local document(s)."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _indexStore.ReplaceAsync(
                Array.AsReadOnly(entries.ToArray()),
                cancellationToken).ConfigureAwait(false);
            return new SemanticResult<int>(
                entries.Count == 0 ? SemanticState.Empty : SemanticState.Ready,
                entries.Count == 0
                    ? "No extracted local content is available to index."
                    : $"{entries.Count} local document(s) indexed.",
                entries.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SemanticResult<int>(
                SemanticState.Cancelled,
                "Semantic indexing was cancelled; no partial replacement was published.",
                0);
        }
        catch (Exception)
        {
            return new SemanticResult<int>(
                SemanticState.Failed,
                "The local semantic index could not be built.",
                0);
        }
    }

    private SemanticIndexEntry CreateEntry(ContentRecord record, string indexFingerprint)
    {
        var metadataTerms = Terms(string.Join(' ', record.Metadata.Select(field => $"{field.Name} {field.Value}")));
        var nativeTerms = Terms(record.NativeText);
        var ocrTerms = Terms(record.OcrText);
        var activeTags = record.Tags
            .Where(tag => tag.AcceptanceState != TagAcceptanceState.Rejected)
            .ToArray();
        var embeddingText = string.Join(
            ' ',
            Path.GetFileName(record.FullPath),
            record.FullPath,
            string.Join(' ', activeTags.Select(tag => tag.DisplayName)),
            string.Join(' ', metadataTerms),
            string.Join(' ', nativeTerms),
            string.Join(' ', ocrTerms));
        return new SemanticIndexEntry(
            record.FullPath,
            record.SourceFingerprint,
            indexFingerprint,
            Path.GetFileName(record.FullPath),
            Array.AsReadOnly(record.Tags.ToArray()),
            metadataTerms,
            nativeTerms,
            ocrTerms,
            _embeddingProvider.Embed(embeddingText),
            _timeProvider.GetUtcNow());
    }

    private static IReadOnlyList<string> Terms(string? text) => Array.AsReadOnly(
        SemanticTokenizer.Tokenize(text, 1024)
            .Distinct(StringComparer.Ordinal)
            .Take(256)
            .ToArray());

    private static string CreateIndexFingerprint(ContentRecord record)
    {
        var value = new StringBuilder(record.SourceFingerprint);
        foreach (var tag in record.Tags.OrderBy(tag => tag.TagId, StringComparer.Ordinal))
        {
            value.Append('|')
                .Append(tag.TagId)
                .Append(':')
                .Append(tag.AcceptanceState)
                .Append(':')
                .Append(tag.UpdatedAtUtc?.UtcTicks ?? 0);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())))
            .ToLowerInvariant();
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
