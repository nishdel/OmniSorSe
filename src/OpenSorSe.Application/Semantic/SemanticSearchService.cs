using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Semantic;

/// <summary>Combines understandable lexical signals with local feature-vector similarity.</summary>
public sealed class SemanticSearchService : ISemanticSearchService
{
    private readonly IConfigurationService _configurationService;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ISemanticIndexStore _indexStore;

    /// <summary>Initializes the bounded local hybrid search service.</summary>
    public SemanticSearchService(
        IConfigurationService configurationService,
        IEmbeddingProvider embeddingProvider,
        ISemanticIndexStore indexStore)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
    }

    /// <inheritdoc />
    public async Task<SemanticResult<IReadOnlyList<SemanticSearchHit>>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var settings = _configurationService.Current.SemanticSearch;
        if (!settings.Enabled)
        {
            return Result(SemanticState.Disabled, "Semantic Search Beta is disabled in Settings.", []);
        }

        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length is 0 or > 256 || normalizedQuery.Any(char.IsControl))
        {
            return Result(SemanticState.Failed, "Enter a search phrase of up to 256 characters.", []);
        }

        var entries = await _indexStore.ListAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return Result(SemanticState.Empty, "The local semantic index is empty. Build it first.", []);
        }

        var queryTokens = SemanticTokenizer.Tokenize(normalizedQuery, 12)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var queryVector = _embeddingProvider.Embed(normalizedQuery);
        var hits = new List<SemanticSearchHit>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var explanation = new List<string>();
            var matchedTags = new List<string>();
            var score = 0d;
            if (string.Equals(entry.FileName, normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                score += 120;
                explanation.Add("Exact filename match");
            }
            else if (queryTokens.Any(token => entry.FileName.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 70;
                explanation.Add("Filename match");
            }

            if (queryTokens.Any(token => entry.FullPath.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 35;
                explanation.Add("Path match");
            }

            foreach (var tag in entry.Tags.Where(tag => tag.AcceptanceState != TagAcceptanceState.Rejected))
            {
                if (!queryTokens.Any(token =>
                        string.Equals(token, tag.NormalizedValue, StringComparison.OrdinalIgnoreCase) ||
                        tag.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var tagScore = tag.AcceptanceState == TagAcceptanceState.Accepted
                    ? tag.Source == TagSource.UserApproved ? 105 : 85
                    : 25 * (tag.Confidence ?? 0.5);
                score += tagScore;
                matchedTags.Add(tag.DisplayName);
            }

            if (matchedTags.Count > 0)
            {
                explanation.Add($"Matched tags: {string.Join(", ", matchedTags.Distinct(StringComparer.OrdinalIgnoreCase))}");
            }

            var metadataMatch = queryTokens.Any(entry.MetadataTerms.Contains);
            var nativeMatch = queryTokens.Any(entry.NativeTextTerms.Contains);
            var ocrMatch = queryTokens.Any(entry.OcrTextTerms.Contains);
            if (metadataMatch)
            {
                score += 50;
                explanation.Add("Matched embedded or filesystem metadata");
            }

            if (nativeMatch)
            {
                score += 20;
                explanation.Add("Matched native text");
            }

            if (ocrMatch)
            {
                score += 10;
                explanation.Add("Matched bounded OCR text");
            }

            var similarity = Cosine(queryVector, entry.Vector);
            if (similarity > 0)
            {
                score += similarity * 30;
                explanation.Add($"Local similarity: {DescribeSimilarity(similarity)}");
            }

            if (score <= 1)
            {
                continue;
            }

            hits.Add(new SemanticSearchHit(
                entry.FullPath,
                entry.FileName,
                Math.Round(score, 3),
                string.Join("; ", explanation),
                Array.AsReadOnly(matchedTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
                metadataMatch,
                nativeMatch,
                ocrMatch));
        }

        var ordered = hits
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.FullPath, PathComparer)
            .Take(settings.MaximumResultCount)
            .ToArray();
        return Result(
            SemanticState.Ready,
            $"{ordered.Length} local result(s). Scores combine exact, tag, metadata, text, and Beta similarity signals.",
            Array.AsReadOnly(ordered));
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count)
        {
            return 0;
        }

        double value = 0;
        for (var index = 0; index < left.Count; index++)
        {
            value += left[index] * right[index];
        }

        return Math.Clamp(value, 0, 1);
    }

    private static string DescribeSimilarity(double similarity) => similarity switch
    {
        >= 0.75 => "strong",
        >= 0.4 => "moderate",
        _ => "weak",
    };

    private static SemanticResult<IReadOnlyList<SemanticSearchHit>> Result(
        SemanticState state,
        string message,
        IReadOnlyList<SemanticSearchHit> value) => new(state, message, value);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
