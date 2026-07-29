namespace OpenSorSe.Application.Semantic;

/// <summary>Defines one deterministic synthetic relevance expectation.</summary>
public sealed record SearchQualityCase(
    string Query,
    IReadOnlySet<string> RelevantFileIds,
    string? ExpectedFirstFileId = null);

/// <summary>Reports bounded relevance metrics for a named synthetic corpus.</summary>
public sealed record SearchQualityMetrics(
    int QueryCount,
    double TopResultCorrectness,
    double TopKRecall,
    double MeanReciprocalRank,
    double ExactMatchPreservation,
    bool StableOrdering);

/// <summary>
/// Evaluates ranker changes against synthetic expectations. Metrics are regression
/// indicators for the supplied corpus, not universal claims about Search quality.
/// </summary>
public sealed class SearchQualityEvaluator
{
    private readonly ISearchQueryInterpreter _interpreter;
    private readonly ISearchRanker _ranker;

    /// <summary>Initializes the evaluator with independently testable Search components.</summary>
    public SearchQualityEvaluator(
        ISearchQueryInterpreter interpreter,
        ISearchRanker ranker)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _ranker = ranker ?? throw new ArgumentNullException(nameof(ranker));
    }

    /// <summary>Computes top-result, top-k recall, reciprocal-rank, exact, and stability metrics.</summary>
    public SearchQualityMetrics Evaluate(
        IReadOnlyList<SearchCandidateDocument> corpus,
        IReadOnlyList<SearchQualityCase> cases,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(cases);
        if (corpus.Count == 0 || cases.Count == 0)
        {
            throw new ArgumentException("A synthetic corpus and at least one relevance case are required.");
        }

        if (topK is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(topK));
        }

        if (cases.Any(item =>
                string.IsNullOrWhiteSpace(item.Query) ||
                item.RelevantFileIds.Count == 0 ||
                item.RelevantFileIds.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("Synthetic relevance cases are invalid.", nameof(cases));
        }

        double topCorrect = 0;
        double recall = 0;
        double reciprocalRank = 0;
        var exactCases = 0;
        var exactCorrect = 0;
        var stable = true;
        foreach (var qualityCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interpretation = _interpreter.Interpret(new SearchRequest(qualityCase.Query));
            var first = _ranker.Rank(
                interpretation,
                corpus,
                Math.Min(
                    SearchLimits.MaximumRankedResults,
                    Math.Max(topK, corpus.Count)),
                cancellationToken);
            var second = _ranker.Rank(
                interpretation,
                corpus,
                Math.Min(
                    SearchLimits.MaximumRankedResults,
                    Math.Max(topK, corpus.Count)),
                cancellationToken);
            stable &= first.Select(item => item.Document.FileId)
                .SequenceEqual(second.Select(item => item.Document.FileId), StringComparer.Ordinal);

            var firstId = first.FirstOrDefault()?.Document.FileId;
            if (firstId is not null &&
                (qualityCase.ExpectedFirstFileId is not null
                    ? string.Equals(firstId, qualityCase.ExpectedFirstFileId, StringComparison.Ordinal)
                    : qualityCase.RelevantFileIds.Contains(firstId)))
            {
                topCorrect++;
            }

            var topIds = first.Take(topK)
                .Select(item => item.Document.FileId)
                .Where(id => id is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            recall += qualityCase.RelevantFileIds.Count(topIds.Contains) /
                (double)qualityCase.RelevantFileIds.Count;
            var firstRelevantRank = first
                .Select((item, index) => new { item.Document.FileId, Rank = index + 1 })
                .FirstOrDefault(item =>
                    item.FileId is not null &&
                    qualityCase.RelevantFileIds.Contains(item.FileId));
            if (firstRelevantRank is not null)
            {
                reciprocalRank += 1d / firstRelevantRank.Rank;
            }

            var exact = corpus.FirstOrDefault(item =>
                string.Equals(
                    SearchTextNormalizer.Normalize(item.FileName),
                    SearchTextNormalizer.Normalize(qualityCase.Query),
                    StringComparison.Ordinal));
            if (exact?.FileId is not null)
            {
                exactCases++;
                exactCorrect += string.Equals(firstId, exact.FileId, StringComparison.Ordinal) ? 1 : 0;
            }
        }

        return new SearchQualityMetrics(
            cases.Count,
            topCorrect / cases.Count,
            recall / cases.Count,
            reciprocalRank / cases.Count,
            exactCases == 0 ? 1 : exactCorrect / (double)exactCases,
            stable);
    }
}
