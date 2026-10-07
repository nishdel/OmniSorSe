using System.Globalization;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Relationships;

/// <summary>A discovery suggestion; it neither creates nor asserts a factual relationship.</summary>
public sealed record SemanticRelatedFile(string FileId, string FileName, string FullPath, string Explanation);

/// <summary>Separately reports optional semantic suggestions and their availability.</summary>
public sealed record SemanticRelatedFilesResult(IReadOnlyList<SemanticRelatedFile> Files, string Message);

/// <summary>Reads rebuildable similarity while honoring durable relationship decisions and privacy.</summary>
public interface ISemanticRelatedFilesService
{
    /// <summary>Returns at most fifty eligible suggestions without altering any catalog or source file.</summary>
    Task<SemanticRelatedFilesResult> GetRelatedAsync(string fileId, CancellationToken cancellationToken = default);
}

/// <summary>Coordinates the model identity, privacy-aware vector query and authoritative document hydration.</summary>
public sealed class SemanticRelatedFilesService(
    IConfigurationService configuration,
    IModelEmbeddingProvider provider,
    IVectorSearchStore vectors,
    IProgressiveSearchDocumentLookup documents) : ISemanticRelatedFilesService
{
    /// <inheritdoc />
    public async Task<SemanticRelatedFilesResult> GetRelatedAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var selectedSettings = configuration.Current.SemanticSearch;
        if (!selectedSettings.Enabled || !selectedSettings.EmbeddingsEnabled)
        {
            return new([], "Semantic similarity is disabled. Evidence-backed relationships remain available.");
        }

        using var queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        queryCancellation.CancelAfter(TimeSpan.FromSeconds(SemanticQueryLimits.MaximumDurationSeconds));
        var queryToken = queryCancellation.Token;
        try
        {
            var model = await provider.GetModelAsync(queryToken).WaitAsync(queryToken).ConfigureAwait(false);
            if (model is null)
            {
                return new([], "The embedding model is unavailable. Evidence-backed relationships remain available.");
            }

            // Source/target privacy and explicit pair rejections are applied by the store before top-k.
            var request = new DiscoverySearchRequest(string.Empty, [], 50);
            var result = await vectors.SearchRelatedVectorsAsync(model, fileId, request, queryToken).WaitAsync(queryToken).ConfigureAwait(false);
            var matches = result.Matches.Where(match => match.FileId != fileId && double.IsFinite(match.Similarity))
                .GroupBy(match => match.FileId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(match => match.Similarity)
                    .ThenBy(match => match.ChunkId, StringComparer.Ordinal).First())
                .OrderByDescending(match => match.Similarity)
                .ThenBy(match => match.FileId, StringComparer.Ordinal).Take(50).ToArray();
            var ids = matches.Select(match => match.FileId).ToArray();
            var hydrated = ids.Length == 0 ? [] : await documents.GetDocumentsByIdsAsync(ids, queryToken).WaitAsync(queryToken).ConfigureAwait(false);
            var byId = hydrated.Where(document => !document.IsExcluded).GroupBy(document => document.FileId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var currentMatches = await vectors.ValidateVectorMatchesAsync(model, result with { Matches = matches }, request,
                fileId, queryToken).WaitAsync(queryToken).ConfigureAwait(false);
            var currentSettings = configuration.Current.SemanticSearch;
            if (!currentSettings.Enabled || !currentSettings.EmbeddingsEnabled ||
                !string.Equals(currentSettings.EmbeddingModel, selectedSettings.EmbeddingModel, StringComparison.Ordinal))
            {
                return new([], "Semantic settings changed. Evidence-backed relationships remain available.");
            }
            var suggestions = currentMatches.Where(match => byId.ContainsKey(match.FileId)).Select(match =>
                new SemanticRelatedFile(match.FileId, byId[match.FileId].FileName, byId[match.FileId].FullPath,
                    $"Semantic similarity only · {model.Model} · cosine {match.Similarity.ToString("F3", CultureInfo.InvariantCulture)} · chunk {match.ChunkId}, {match.Field} characters {match.Start}–{match.Start + match.Length}"))
                .ToArray();
            return new(suggestions, suggestions.Length == 0
                ? "No compatible semantic suggestions are ready for this file. Indexing may still be in progress."
                : $"{suggestions.Length:N0} semantic suggestions. Similar wording or meaning does not establish a factual relationship.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (queryCancellation.IsCancellationRequested)
        {
            return new([], "Semantic similarity exceeded its 5-second query budget. Evidence-backed relationships remain available.");
        }
        catch (Exception)
        {
            return new([], "Semantic similarity is temporarily unavailable. Evidence-backed relationships remain available.");
        }
    }
}
