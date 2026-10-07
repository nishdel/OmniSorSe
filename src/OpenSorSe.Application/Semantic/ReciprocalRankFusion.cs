using System.Globalization;

namespace OpenSorSe.Application.Semantic;

/// <summary>Combines independent keyword and learned-vector rankings by stable catalog identity.</summary>
public static class ReciprocalRankFusion
{
    /// <summary>Conventional RRF smoothing constant; raw cosine and lexical scores are never added.</summary>
    public const int RankConstant = 60;

    /// <summary>Fuses eligible hydrated results, preserving exact filename intent ahead of discovery results.</summary>
    public static IReadOnlyList<RankedSearchCandidate> Fuse(
        IReadOnlyList<RankedSearchCandidate> keyword,
        IReadOnlyList<SearchCandidateDocument> vectorDocuments,
        IReadOnlyList<VectorSearchMatch> matches,
        SemanticModelIdentity model,
        int maximumResults)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumResults, 1);
        var documents = vectorDocuments.Where(document => document.FileId is not null)
            .GroupBy(document => document.FileId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var documentsByPath = documents.Values.GroupBy(document => document.FullPath,
                OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparer)
            .ToDictionary(group => group.Key, group => group.First(),
                OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparer);
        var values = new Dictionary<string, FusionValue>(StringComparer.Ordinal);
        foreach (var candidate in keyword)
        {
            var current = candidate.Document.FileId is null && documentsByPath.TryGetValue(candidate.Document.FullPath, out var catalog)
                ? candidate with { Document = catalog } : candidate;
            var key = Identity(current.Document);
            if (!values.ContainsKey(key))
            {
                values[key] = new FusionValue(current, values.Count + 1, null);
            }
        }

        // A file contributes once, regardless of how many chunks happened to match.
        var vectorRank = 0;
        foreach (var match in matches.Where(match => double.IsFinite(match.Similarity))
                     .OrderByDescending(match => match.Similarity)
                     .ThenBy(match => match.FileId, StringComparer.Ordinal)
                     .ThenBy(match => match.ChunkId, StringComparer.Ordinal)
                     .GroupBy(match => match.FileId, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            if (!documents.TryGetValue(match.FileId, out var document))
            {
                continue;
            }

            vectorRank++;
            var key = Identity(document);
            var existing = values.GetValueOrDefault(key);
            var components = existing?.Candidate.Components.ToList() ?? [];
            components.Add(new SearchRankingComponent(
                SearchRankingSignalKind.SemanticSimilarity,
                match.Field,
                1d / (RankConstant + vectorRank),
                $"Semantic similarity (not a factual relationship): {model.Model}; cosine {match.Similarity.ToString("F3", CultureInfo.InvariantCulture)}; chunk {match.ChunkId}, {match.Field} characters {match.Start}–{match.Start + match.Length}",
                match.Text[..Math.Min(match.Text.Length, SearchLimits.MaximumSnippetCharacters)]));
            var snippet = existing?.Candidate.Snippet ?? new SearchSnippet(
                SearchSnippetSource.Chunk,
                $"Semantic match · {match.Field} · offset {match.Start}",
                match.Text[..Math.Min(match.Text.Length, SearchLimits.MaximumSnippetCharacters)],
                []);
            values[key] = new FusionValue(
                new RankedSearchCandidate(document, 0, components.AsReadOnly(), snippet),
                existing?.KeywordRank,
                vectorRank);
        }

        return values.Values.Select(value =>
            {
                var score = (value.KeywordRank is { } keywordRank ? 1d / (RankConstant + keywordRank) : 0) +
                            (value.VectorRank is { } semanticRank ? 1d / (RankConstant + semanticRank) : 0);
                var explanation = $"Hybrid reciprocal rank fusion (k={RankConstant}): keyword rank {FormatRank(value.KeywordRank)}, semantic rank {FormatRank(value.VectorRank)}" +
                    (ExactFilenameTier(value.Candidate.Components) > 0 ? "; exact filename intent has priority" : string.Empty);
                return value.Candidate with
                {
                    Score = score,
                    Components = Array.AsReadOnly(value.Candidate.Components.Append(new SearchRankingComponent(
                        SearchRankingSignalKind.HybridFusion, "hybrid ranking", score, explanation)).ToArray()),
                };
            })
            .OrderByDescending(candidate => ExactFilenameTier(candidate.Components))
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Document.FullPath, StringComparer.Ordinal)
            .Take(maximumResults)
            .ToArray();
    }

    private static string FormatRank(int? rank) => rank?.ToString(CultureInfo.InvariantCulture) ?? "absent";

    private static string Identity(SearchCandidateDocument document) => document.FileId is { } id
        ? $"id:{id}" : $"path:{document.FullPath}";

    private static int ExactFilenameTier(IReadOnlyList<SearchRankingComponent> components) =>
        components.Any(component => component.Kind == SearchRankingSignalKind.ExactFilename) ? 2 :
        components.Any(component => component.Kind == SearchRankingSignalKind.ExactFilenameStem) ? 1 : 0;

    private sealed record FusionValue(RankedSearchCandidate Candidate, int? KeywordRank, int? VectorRank);
}
