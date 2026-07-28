using OpenSorSe.Application.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Evaluates deterministic, metadata-aware queries over one immutable result snapshot without filesystem access.
/// </summary>
public static class ResultsQueryEngine
{
    private static readonly int[] ApprovedPageSizes = [50, 100, 200, 500];
    private static readonly ResultSearchMatch NoTextMatch = new(0, "No text query applied.");

    /// <summary>
    /// Evaluates a normalized query with deterministic ranking, filters, and bounded paging.
    /// </summary>
    /// <param name="snapshot">The immutable completed scan snapshot.</param>
    /// <param name="query">The user query to normalize and evaluate.</param>
    /// <param name="cancellationToken">Cancels only this local query evaluation.</param>
    /// <param name="tagsByFile">Accepted application-owned tags for the current in-memory result session.</param>
    /// <returns>A normalized query and a bounded, stable page.</returns>
    public static ResultsQueryResult Evaluate(
        ResultsSnapshot snapshot,
        ResultsQuery? query,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, IReadOnlyList<TagAssociation>>? tagsByFile = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var normalizedQuery = Normalize(query);
        var tokens = Tokenize(normalizedQuery.Text);
        var filtered = new List<SearchCandidate>(snapshot.Files.Count);
        for (var index = 0; index < snapshot.Files.Count; index++)
        {
            if ((index & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var file = snapshot.Files[index];
            var tags = tagsByFile is not null && tagsByFile.TryGetValue(file.Id, out var values)
                ? values
                : Array.Empty<TagAssociation>();
            if (Matches(file, tags, normalizedQuery, tokens, out var match))
            {
                filtered.Add(new SearchCandidate(file, match));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = Order(filtered, normalizedQuery);
        var totalItemCount = ordered.Count;
        var totalPageCount = totalItemCount == 0 ? 0 : (int)Math.Ceiling(totalItemCount / (double)normalizedQuery.PageSize);
        var pageIndex = totalPageCount == 0 ? 0 : Math.Min(normalizedQuery.PageIndex, totalPageCount - 1);
        normalizedQuery = normalizedQuery with { PageIndex = pageIndex };
        var pageCandidates = totalItemCount == 0
            ? Array.Empty<SearchCandidate>()
            : ordered.Skip(pageIndex * normalizedQuery.PageSize).Take(normalizedQuery.PageSize).ToArray();
        var pageItems = pageCandidates.Select(candidate => candidate.File).ToArray();
        var matches = pageCandidates.ToDictionary(candidate => candidate.File.Id, candidate => candidate.Match, StringComparer.Ordinal);
        return new ResultsQueryResult(
            normalizedQuery,
            new ResultsPage(Array.AsReadOnly(pageItems), pageIndex, normalizedQuery.PageSize, totalItemCount, totalPageCount)
            {
                Matches = matches,
            });
    }

    /// <summary>
    /// Normalizes malformed query values to safe documented defaults.
    /// </summary>
    /// <param name="query">The query to normalize.</param>
    /// <returns>A safe query value.</returns>
    public static ResultsQuery Normalize(ResultsQuery? query)
    {
        query ??= ResultsQuery.Default;
        var duplicateFilter = Enum.IsDefined(query.DuplicateFilter) ? query.DuplicateFilter : ResultDuplicateFilter.All;
        var plannedOperationFilter = Enum.IsDefined(query.PlannedOperationFilter) ? query.PlannedOperationFilter : ResultPlannedOperationFilter.All;
        var sortField = Enum.IsDefined(query.SortField) ? query.SortField : ResultsSortField.Name;
        var sortDirection = Enum.IsDefined(query.SortDirection) ? query.SortDirection : SortDirection.Ascending;
        var category = query.Category is { } value && !Enum.IsDefined(value) ? null : query.Category;
        var extension = NormalizeExtension(query.Extension);
        return new ResultsQuery(
            string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            duplicateFilter,
            extension,
            category,
            plannedOperationFilter,
            sortField,
            sortDirection,
            Math.Max(0, query.PageIndex),
            ApprovedPageSizes.Contains(query.PageSize) ? query.PageSize : ResultsQuery.Default.PageSize,
            string.IsNullOrWhiteSpace(query.DuplicateGroupId) ? null : query.DuplicateGroupId);
    }

    private static bool Matches(
        ResultFile file,
        IReadOnlyList<TagAssociation> tags,
        ResultsQuery query,
        IReadOnlyList<string> tokens,
        out ResultSearchMatch match)
    {
        if (!MatchesDuplicate(file, query.DuplicateFilter) ||
            query.Extension is not null &&
            !string.Equals(file.NormalizedExtension, query.Extension, StringComparison.Ordinal) ||
            !MatchesCategory(file, query.Category) ||
            !MatchesPlannedOperation(file, query.PlannedOperationFilter) ||
            query.DuplicateGroupId is not null &&
            !string.Equals(file.DuplicateGroupId, query.DuplicateGroupId, StringComparison.Ordinal))
        {
            match = default!;
            return false;
        }

        return MatchesText(file, tags, tokens, out match);
    }

    private static bool MatchesText(
        ResultFile file,
        IReadOnlyList<TagAssociation> tags,
        IReadOnlyList<string> tokens,
        out ResultSearchMatch match)
    {
        if (tokens.Count == 0)
        {
            match = NoTextMatch;
            return true;
        }

        var totalScore = 0;
        var explanations = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            var signal = GetBestSignal(file, tags, token);
            if (signal.Score == 0)
            {
                match = default!;
                return false;
            }

            totalScore += signal.Score;
            explanations.Add(signal.Explanation);
        }

        match = new ResultSearchMatch(totalScore, string.Join("; ", explanations.Distinct(StringComparer.Ordinal)));
        return true;
    }

    private static SearchSignal GetBestSignal(ResultFile file, IReadOnlyList<TagAssociation> tags, string token)
    {
        var best = default(SearchSignal);
        if (string.Equals(file.DisplayFileName, token, StringComparison.OrdinalIgnoreCase))
        {
            best = Better(best, 120, $"exact filename match: {token}");
        }
        else if (file.DisplayFileName.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            best = Better(best, 100, $"filename starts with: {token}");
        }
        else if (file.DisplayFileName.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            best = Better(best, 80, $"filename contains: {token}");
        }

        if (string.Equals(file.NormalizedExtension, token, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file.NormalizedExtension.TrimStart('.'), token, StringComparison.OrdinalIgnoreCase))
        {
            best = Better(best, 70, $"extension match: {file.NormalizedExtension}");
        }

        if (file.ClassificationDisplay.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            best = Better(best, 60, $"category match: {file.ClassificationDisplay}");
        }

        if (file.FullPath.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            best = Better(best, 45, $"path match: {token}");
        }

        foreach (var tag in tags.Where(tag => tag.AcceptanceState == TagAcceptanceState.Accepted))
        {
            if (string.Equals(tag.NormalizedValue, token, StringComparison.OrdinalIgnoreCase) || string.Equals(tag.DisplayName, token, StringComparison.OrdinalIgnoreCase))
            {
                best = Better(best, 90, $"tag match: {tag.DisplayName}");
            }
            else if (tag.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase) || tag.NormalizedValue.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                best = Better(best, 75, $"tag match: {tag.DisplayName}");
            }
        }

        return best;
    }

    private static SearchSignal Better(
        SearchSignal current,
        int score,
        string explanation) =>
        score > current.Score ||
        score == current.Score &&
        string.Compare(explanation, current.Explanation, StringComparison.Ordinal) < 0
            ? new SearchSignal(score, explanation)
            : current;

    private static List<SearchCandidate> Order(List<SearchCandidate> candidates, ResultsQuery query)
    {
        var sortField = query.Text is not null && query.SortField == ResultsSortField.Name
            ? ResultsSortField.Relevance
            : query.SortField;
        var sortDirection = sortField == ResultsSortField.Relevance && query.SortField == ResultsSortField.Name
            ? SortDirection.Descending
            : query.SortDirection;
        IOrderedEnumerable<SearchCandidate> ordered = sortField switch
        {
            ResultsSortField.Relevance => candidates.OrderByDescending(candidate => candidate.Match.Score),
            ResultsSortField.Name => candidates.OrderBy(candidate => candidate.File.DisplayFileName, StringComparer.OrdinalIgnoreCase),
            ResultsSortField.Path => candidates.OrderBy(candidate => candidate.File.FullPath, StringComparer.OrdinalIgnoreCase),
            ResultsSortField.Extension => candidates.OrderBy(candidate => candidate.File.NormalizedExtension, StringComparer.OrdinalIgnoreCase),
            ResultsSortField.Size => candidates.OrderBy(candidate => candidate.File.SizeInBytes is null).ThenBy(candidate => candidate.File.SizeInBytes),
            ResultsSortField.ModifiedTime => candidates.OrderBy(candidate => candidate.File.LastWriteTimeUtc is null).ThenBy(candidate => candidate.File.LastWriteTimeUtc),
            ResultsSortField.DuplicateState => candidates.OrderBy(candidate => candidate.File.DuplicateStatus),
            _ => throw new InvalidOperationException("The query sort field was not normalized."),
        };

        if (sortDirection == SortDirection.Ascending && sortField == ResultsSortField.Relevance)
        {
            ordered = candidates.OrderBy(candidate => candidate.Match.Score);
        }
        else if (sortDirection == SortDirection.Descending && sortField != ResultsSortField.Relevance)
        {
            ordered = sortField switch
            {
                ResultsSortField.Name => candidates.OrderByDescending(candidate => candidate.File.DisplayFileName, StringComparer.OrdinalIgnoreCase),
                ResultsSortField.Path => candidates.OrderByDescending(candidate => candidate.File.FullPath, StringComparer.OrdinalIgnoreCase),
                ResultsSortField.Extension => candidates.OrderByDescending(candidate => candidate.File.NormalizedExtension, StringComparer.OrdinalIgnoreCase),
                ResultsSortField.Size => candidates.OrderBy(candidate => candidate.File.SizeInBytes is not null).ThenByDescending(candidate => candidate.File.SizeInBytes),
                ResultsSortField.ModifiedTime => candidates.OrderBy(candidate => candidate.File.LastWriteTimeUtc is not null).ThenByDescending(candidate => candidate.File.LastWriteTimeUtc),
                ResultsSortField.DuplicateState => candidates.OrderByDescending(candidate => candidate.File.DuplicateStatus),
                _ => throw new InvalidOperationException("The query sort field was not normalized."),
            };
        }

        return ordered
            .ThenBy(candidate => candidate.File.FullPath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.File.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool MatchesDuplicate(ResultFile file, ResultDuplicateFilter filter) => filter switch
    {
        ResultDuplicateFilter.All => true,
        ResultDuplicateFilter.ExactDuplicatesOnly => file.DuplicateStatus == DuplicateStatus.Duplicate,
        ResultDuplicateFilter.UniqueOnly => file.DuplicateStatus == DuplicateStatus.Unique,
        ResultDuplicateFilter.UnknownOrUnavailable => file.DuplicateStatus == DuplicateStatus.Unknown,
        _ => false,
    };

    private static bool MatchesCategory(ResultFile file, FileCategory? category)
    {
        if (category is null)
        {
            return true;
        }

        return category == FileCategory.Unknown
            ? file.Category is null or FileCategory.Unknown
            : file.Category == category;
    }

    private static bool MatchesPlannedOperation(ResultFile file, ResultPlannedOperationFilter filter) => filter switch
    {
        ResultPlannedOperationFilter.All => true,
        ResultPlannedOperationFilter.HasAcceptedOperation => file.HasPlannedOperation,
        ResultPlannedOperationFilter.NoAcceptedOperation => !file.HasPlannedOperation,
        _ => false,
    };

    private static IReadOnlyList<string> Tokenize(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : Array.AsReadOnly(query
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(token => token.Trim().ToLowerInvariant())
        .Where(token => token.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .Take(12)
        .ToArray());

    private static string? NormalizeExtension(string? extension)
    {
        if (extension is null)
        {
            return null;
        }

        var trimmed = extension.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return $".{trimmed.TrimStart('.').ToLowerInvariant()}";
    }

    private readonly record struct SearchCandidate(ResultFile File, ResultSearchMatch Match);

    private readonly record struct SearchSignal(int Score, string Explanation);
}
