using System.Globalization;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.ContentIntelligence;

namespace OpenSorSe.Application.Semantic;

/// <summary>Maps extensions to stable plain-language Search categories.</summary>
public static class SearchFileTypeClassifier
{
    private static readonly HashSet<string> Documents = new(
        [".doc", ".docx", ".odt", ".rtf", ".txt", ".text", ".md", ".markdown"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Spreadsheets = new(
        [".csv", ".tsv", ".xls", ".xlsx", ".ods"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Presentations = new(
        [".ppt", ".pptx", ".odp"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Images = new(
        [".bmp", ".gif", ".heic", ".jpeg", ".jpg", ".png", ".svg", ".tif", ".tiff", ".webp"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Videos = new(
        [".avi", ".m4v", ".mkv", ".mov", ".mp4", ".webm"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Audio = new(
        [".aac", ".flac", ".m4a", ".mp3", ".ogg", ".wav"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Archives = new(
        [".7z", ".bz2", ".gz", ".rar", ".tar", ".xz", ".zip"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a stable category for a filename or extension.</summary>
    public static string Classify(string value)
    {
        var extension = value.StartsWith(".", StringComparison.Ordinal)
            ? value
            : Path.GetExtension(value);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "pdf";
        }

        if (Documents.Contains(extension))
        {
            return "document";
        }

        if (Spreadsheets.Contains(extension))
        {
            return "spreadsheet";
        }

        if (Presentations.Contains(extension))
        {
            return "presentation";
        }

        if (Images.Contains(extension))
        {
            return "image";
        }

        if (Videos.Contains(extension))
        {
            return "video";
        }

        if (Audio.Contains(extension))
        {
            return "audio";
        }

        if (Archives.Contains(extension))
        {
            return "archive";
        }

        return "other";
    }
}

/// <summary>
/// Applies tiered deterministic ranking. Exact and literal tiers are ordered before
/// semantic similarity, while recency, source priority, and completeness only break
/// otherwise comparable results.
/// </summary>
public sealed class HybridSearchRanker : ISearchRanker
{
    private const double MinimumSemanticSimilarity = 0.20;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ISearchSnippetFactory _snippetFactory;

    /// <summary>Initializes the ranker with local deterministic feature and snippet providers.</summary>
    public HybridSearchRanker(
        IEmbeddingProvider embeddingProvider,
        ISearchSnippetFactory snippetFactory)
    {
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _snippetFactory = snippetFactory ?? throw new ArgumentNullException(nameof(snippetFactory));
    }

    /// <inheritdoc />
    public IReadOnlyList<RankedSearchCandidate> Rank(
        SearchInterpretation interpretation,
        IReadOnlyList<SearchCandidateDocument> candidates,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        ArgumentNullException.ThrowIfNull(candidates);
        if (maximumResults is < 1 or > SearchLimits.MaximumRankedResults)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }

        var normalizedTopic = SearchTextNormalizer.Normalize(interpretation.TopicText);
        var tokens = interpretation.TopicTokens
            .Select(SearchTextNormalizer.Normalize)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(SearchLimits.MaximumQueryTokens)
            .ToArray();
        var queryVector = normalizedTopic.Length == 0
            ? []
            : _embeddingProvider.Embed(normalizedTopic);
        var ranked = new List<RankedValue>(Math.Min(candidates.Count, maximumResults * 4));
        var fuzzyEvaluations = 0;
        foreach (var candidate in candidates.OrderBy(item => item.FullPath, StringComparer.Ordinal))
        {
            if ((fuzzyEvaluations & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var components = new List<SearchRankingComponent>();
            if (!MatchesFilters(candidate, interpretation.Filters, components))
            {
                continue;
            }

            var fields = CandidateFields.Create(candidate);
            var literalScore = 0d;
            var rankClass = 0;
            if (normalizedTopic.Length > 0 &&
                string.Equals(fields.FileName, normalizedTopic, StringComparison.Ordinal))
            {
                rankClass = 7;
                literalScore += Add(
                    components,
                    SearchRankingSignalKind.ExactFilename,
                    "filename",
                    1000,
                    "Exact filename match",
                    candidate.FileName);
            }
            else if (normalizedTopic.Length > 0 &&
                     string.Equals(fields.FileNameStem, normalizedTopic, StringComparison.Ordinal))
            {
                rankClass = 6;
                literalScore += Add(
                    components,
                    SearchRankingSignalKind.ExactFilenameStem,
                    "filename",
                    800,
                    "Exact filename match without the extension",
                    Path.GetFileNameWithoutExtension(candidate.FileName));
            }
            else
            {
                if (normalizedTopic.Length >= 2 &&
                    fields.FileNameStem.StartsWith(normalizedTopic, StringComparison.Ordinal))
                {
                    rankClass = 5;
                    literalScore += Add(
                        components,
                        SearchRankingSignalKind.FilenamePrefix,
                        "filename",
                        420,
                        "Filename starts with the Search phrase",
                        interpretation.TopicText);
                }
                else if (normalizedTopic.Length >= 3 &&
                         fields.FileNameStem.Contains(normalizedTopic, StringComparison.Ordinal))
                {
                    rankClass = 4;
                    literalScore += Add(
                        components,
                        SearchRankingSignalKind.FilenameSubstring,
                        "filename",
                        320,
                        "Filename contains the Search phrase",
                        interpretation.TopicText);
                }

                if (normalizedTopic.Length >= 3)
                {
                    var phrase = BestPhraseField(fields, normalizedTopic);
                    if (phrase is not null)
                    {
                        rankClass = Math.Max(rankClass, 4);
                        literalScore += Add(
                            components,
                            SearchRankingSignalKind.ExactPhrase,
                            phrase.Value.Name,
                            300,
                            $"Exact phrase matched {phrase.Value.Label}",
                            interpretation.TopicText);
                    }
                }

                var filenameMatches = CountMatches(fields.FileName, tokens);
                if (filenameMatches > 0)
                {
                    rankClass = Math.Max(rankClass, filenameMatches == tokens.Length ? 4 : 2);
                    literalScore += Add(
                        components,
                        SearchRankingSignalKind.FilenameToken,
                        "filename",
                        180 + (filenameMatches - 1) * 20,
                        filenameMatches == tokens.Length
                            ? "All topic terms matched the filename"
                            : "Filename contains topic terms",
                        FirstMatchingToken(fields.FileName, tokens));
                }

                literalScore += AddFieldMatch(
                    fields.FolderName,
                    tokens,
                    components,
                    SearchRankingSignalKind.FolderName,
                    "folder",
                    "folder name",
                    120,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.Path,
                    tokens,
                    components,
                    SearchRankingSignalKind.Path,
                    "path",
                    "folder path",
                    45,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.Extension,
                    tokens,
                    components,
                    SearchRankingSignalKind.Extension,
                    "extension",
                    "file extension",
                    65,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.FileType,
                    tokens,
                    components,
                    SearchRankingSignalKind.FileType,
                    "file type",
                    "file type",
                    55,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.Tags,
                    tokens,
                    components,
                    SearchRankingSignalKind.Tag,
                    "tags",
                    "tag",
                    150,
                    ref rankClass);
                literalScore += AddSmartTagMatches(candidate.SmartTags, tokens, components, ref rankClass);
                literalScore += AddFieldMatch(
                    fields.Keywords,
                    tokens,
                    components,
                    SearchRankingSignalKind.Keyword,
                    "keywords",
                    "keyword",
                    110,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.ContentTopics,
                    tokens,
                    components,
                    SearchRankingSignalKind.ContentTopic,
                    "content topics",
                    "derived topic",
                    130,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.ContentEntities,
                    tokens,
                    components,
                    SearchRankingSignalKind.ContentEntity,
                    "content entities",
                    "textual entity",
                    140,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.ContentIntelligenceSummary,
                    tokens,
                    components,
                    SearchRankingSignalKind.ContentIntelligenceSummary,
                    "content summary",
                    "source-grounded summary",
                    85,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.Metadata,
                    tokens,
                    components,
                    SearchRankingSignalKind.Metadata,
                    "metadata",
                    "metadata",
                    90,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.MediaMetadata,
                    tokens,
                    components,
                    SearchRankingSignalKind.MediaMetadata,
                    "media metadata",
                    "image, audio, or video metadata",
                    100,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.MediaTranscript,
                    tokens,
                    components,
                    SearchRankingSignalKind.MediaTranscript,
                    "media transcript",
                    "audio or video transcript",
                    105,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.MediaOcr,
                    tokens,
                    components,
                    SearchRankingSignalKind.MediaOcr,
                    "media OCR",
                    "image or video-frame OCR",
                    95,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.MediaVisualDescription,
                    tokens,
                    components,
                    SearchRankingSignalKind.MediaVisualDescription,
                    "visual description",
                    "optional visual description",
                    50,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.ExtractedText,
                    tokens,
                    components,
                    SearchRankingSignalKind.ExtractedText,
                    "document text",
                    "document text",
                    105,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.OcrText,
                    tokens,
                    components,
                    SearchRankingSignalKind.OcrText,
                    "OCR text",
                    "OCR text",
                    95,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.Summary,
                    tokens,
                    components,
                    SearchRankingSignalKind.Summary,
                    "summary",
                    "summary",
                    80,
                    ref rankClass);
                literalScore += AddFieldMatch(
                    fields.Chunks,
                    tokens,
                    components,
                    SearchRankingSignalKind.Chunk,
                    "selected text",
                    "selected document text",
                    75,
                    ref rankClass);
            }

            if (rankClass == 0 &&
                tokens.Length is > 0 and <= 4 &&
                tokens.All(token => token.Length >= 4) &&
                fuzzyEvaluations < SearchLimits.MaximumFuzzyCandidates)
            {
                fuzzyEvaluations++;
                var fuzzy = FindFuzzyFilenameMatch(fields.FileNameTokens, tokens);
                if (fuzzy is not null)
                {
                    rankClass = 1;
                    literalScore += Add(
                        components,
                        SearchRankingSignalKind.FuzzyFilename,
                        "filename",
                        60,
                        "Filename is a close spelling match",
                        fuzzy);
                }
            }

            if (rankClass == 0 && candidate.RelationshipContext is { } relationshipContext)
            {
                rankClass = 1;
                var component = relationshipContext.ToRankingComponent();
                components.Add(component);
                literalScore += 35 + component.Contribution * 5;
            }
            else if (rankClass == 0 && candidate.GraphContext is { } graphContext)
            {
                rankClass = 1;
                var component = graphContext.ToRankingComponent();
                components.Add(component);
                literalScore += 25 + component.Contribution * 5;
            }

            var semanticSimilarity = Cosine(queryVector, candidate.SemanticRepresentation);
            if (semanticSimilarity >= MinimumSemanticSimilarity)
            {
                components.Add(new SearchRankingComponent(
                    SearchRankingSignalKind.SemanticSimilarity,
                    "related concepts",
                    Math.Round(semanticSimilarity, 4),
                    $"Related concepts: {DescribeSimilarity(semanticSimilarity)}"));
            }

            var filtersOnly = tokens.Length == 0 && interpretation.Filters.Count > 0;
            if (!filtersOnly && rankClass == 0 && semanticSimilarity < MinimumSemanticSimilarity)
            {
                continue;
            }

            AddTieBreakComponents(candidate, components);
            var score = rankClass * 1_000_000d + literalScore * 100d + semanticSimilarity * 10d;
            var snippet = _snippetFactory.Create(candidate, interpretation, components);
            ranked.Add(new RankedValue(
                new RankedSearchCandidate(
                    candidate,
                    Math.Round(score, 3),
                    Array.AsReadOnly(components.ToArray()),
                    snippet),
                rankClass,
                literalScore,
                semanticSimilarity));
        }

        return Array.AsReadOnly(
            ranked
                .OrderByDescending(item => item.RankClass)
                .ThenByDescending(item => item.LiteralScore)
                .ThenByDescending(item => item.SemanticSimilarity)
                .ThenByDescending(item => item.Result.Document.SourcePriority)
                .ThenByDescending(item => item.Result.Document.IsFullyIndexed)
                .ThenByDescending(item => item.Result.Document.ModifiedTimeUtc)
                .ThenBy(item => item.Result.Document.FullPath, StringComparer.Ordinal)
                .Take(maximumResults)
                .Select(item => item.Result)
                .ToArray());
    }

    private static bool MatchesFilters(
        SearchCandidateDocument candidate,
        IReadOnlyList<SearchFilter> filters,
        ICollection<SearchRankingComponent> components)
    {
        var typedSmartTagFilters = filters
            .Where(filter => filter.Kind is SearchFilterKind.SmartTagTheme or
                SearchFilterKind.SmartTagDocumentType or SearchFilterKind.SmartTagUser)
            .GroupBy(filter => filter.Kind)
            .ToArray();
        foreach (var group in typedSmartTagFilters)
        {
            var matched = group.FirstOrDefault(filter => MatchesSmartTagFilter(candidate, filter));
            if (matched is null)
            {
                return false;
            }

            components.Add(new SearchRankingComponent(
                SearchRankingSignalKind.Filter,
                matched.Kind.ToString(),
                1,
                $"{matched.DisplayName} matched"));
        }

        var canonicalFacetFilters = filters
            .Where(filter => filter.Kind is SearchFilterKind.FileType or
                SearchFilterKind.CreatedYear or SearchFilterKind.ModifiedYear or
                SearchFilterKind.UnresolvedModerateSmartTag)
            .GroupBy(filter => filter.Kind)
            .ToArray();
        foreach (var group in canonicalFacetFilters)
        {
            var matched = group.FirstOrDefault(filter => MatchesSingleFilter(candidate, filter));
            if (matched is null)
            {
                return false;
            }

            components.Add(new SearchRankingComponent(
                SearchRankingSignalKind.Filter,
                matched.Kind.ToString(),
                1,
                $"{matched.DisplayName} matched"));
        }

        foreach (var filter in filters.Where(filter => filter.Kind is not
                     SearchFilterKind.SmartTagTheme and not
                     SearchFilterKind.SmartTagDocumentType and not
                     SearchFilterKind.SmartTagUser and not
                     SearchFilterKind.FileType and not
                     SearchFilterKind.CreatedYear and not
                     SearchFilterKind.ModifiedYear and not
                     SearchFilterKind.UnresolvedModerateSmartTag))
        {
            var matches = MatchesSingleFilter(candidate, filter);
            if (!matches)
            {
                return false;
            }

            components.Add(new SearchRankingComponent(
                SearchRankingSignalKind.Filter,
                filter.Kind.ToString(),
                1,
                $"{filter.DisplayName} matched"));
        }

        return true;
    }

    private static bool MatchesSingleFilter(SearchCandidateDocument candidate, SearchFilter filter) =>
        filter.Kind switch
        {
            SearchFilterKind.FileType =>
                EqualsNormalized(candidate.FileType, filter.Value),
            SearchFilterKind.Extension =>
                EqualsNormalized(candidate.Extension.TrimStart('.'), filter.Value.TrimStart('.')),
            SearchFilterKind.CreatedOnOrAfter =>
                CompareDate(candidate.CreationTimeUtc, filter.Value, onOrAfter: true),
            SearchFilterKind.CreatedBefore =>
                CompareDate(candidate.CreationTimeUtc, filter.Value, onOrAfter: false),
            SearchFilterKind.ModifiedOnOrAfter =>
                CompareDate(candidate.ModifiedTimeUtc, filter.Value, onOrAfter: true),
            SearchFilterKind.ModifiedBefore =>
                CompareDate(candidate.ModifiedTimeUtc, filter.Value, onOrAfter: false),
            SearchFilterKind.MinimumSizeBytes =>
                CompareSize(candidate.Length, filter.Value, minimum: true),
            SearchFilterKind.MaximumSizeBytes =>
                CompareSize(candidate.Length, filter.Value, minimum: false),
            SearchFilterKind.Source =>
                ContainsNormalized(candidate.SourceName, filter.Value) ||
                EqualsNormalized(candidate.SourceId, filter.Value),
            SearchFilterKind.Folder =>
                ContainsNormalized(candidate.FolderName, filter.Value) ||
                ContainsNormalized(candidate.RelativePath, filter.Value),
            SearchFilterKind.Tag =>
                candidate.Tags.Concat(candidate.Keywords)
                    .Any(value => ContainsNormalized(value, filter.Value)),
            SearchFilterKind.IndexingLevel =>
                candidate.IndexingLevel.HasValue &&
                EqualsNormalized(candidate.IndexingLevel.Value.ToString(), filter.Value),
            SearchFilterKind.IndexingCompletion =>
                filter.Value.Equals(candidate.IsFullyIndexed ? "full" : "partial", StringComparison.OrdinalIgnoreCase),
            SearchFilterKind.OcrAvailability =>
                CompareBoolean(
                    !string.IsNullOrWhiteSpace(candidate.OcrText) ||
                    !string.IsNullOrWhiteSpace(candidate.MediaEvidence?.OcrText),
                    filter.Value),
            SearchFilterKind.SemanticAvailability =>
                CompareBoolean(candidate.SemanticRepresentation is { Count: > 0 }, filter.Value),
            SearchFilterKind.FailureState =>
                CompareBoolean(candidate.HasIndexingFailure, filter.Value),
            SearchFilterKind.CreatedYear =>
                MatchesYear(candidate.CreationTimeUtc, filter.Value),
            SearchFilterKind.ModifiedYear =>
                MatchesYear(candidate.ModifiedTimeUtc, filter.Value),
            SearchFilterKind.UnresolvedModerateSmartTag =>
                candidate.SmartTags.Any(tag =>
                    tag.Confidence == ContentIntelligenceConfidence.Moderate &&
                    tag.State == SmartTagAssignmentState.Suggested &&
                    tag.Decision == SmartTagDecision.None),
            _ => false,
        };

    private static bool MatchesYear(DateTimeOffset? value, string expected) =>
        value.HasValue &&
        int.TryParse(expected, NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
        value.Value.Year == year;

    private static bool MatchesSmartTagFilter(SearchCandidateDocument candidate, SearchFilter filter)
    {
        var expectedType = filter.Kind switch
        {
            SearchFilterKind.SmartTagTheme => SmartTagType.Theme,
            SearchFilterKind.SmartTagDocumentType => SmartTagType.DocumentType,
            SearchFilterKind.SmartTagUser => SmartTagType.UserTag,
            _ => (SmartTagType?)null,
        };
        return expectedType.HasValue && candidate.SmartTags.Any(tag =>
            tag.Definition.Type == expectedType.Value &&
            string.Equals(tag.Definition.TagId, filter.Value, StringComparison.Ordinal) &&
            tag.Decision != SmartTagDecision.Rejected &&
            (tag.State is SmartTagAssignmentState.Accepted or SmartTagAssignmentState.Automatic));
    }

    private static bool CompareDate(DateTimeOffset? candidate, string value, bool onOrAfter) =>
        candidate.HasValue &&
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var boundary) &&
        (onOrAfter ? candidate.Value >= boundary : candidate.Value < boundary);

    private static bool CompareSize(long candidate, string value, bool minimum) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var boundary) &&
        (minimum ? candidate >= boundary : candidate <= boundary);

    private static bool CompareBoolean(bool candidate, string value) =>
        bool.TryParse(value, out var expected) && candidate == expected;

    private static bool EqualsNormalized(string? left, string? right) =>
        string.Equals(
            SearchTextNormalizer.Normalize(left),
            SearchTextNormalizer.Normalize(right),
            StringComparison.Ordinal);

    private static bool ContainsNormalized(string? value, string expected)
    {
        var normalizedExpected = SearchTextNormalizer.Normalize(expected);
        return normalizedExpected.Length > 0 &&
            SearchTextNormalizer.Normalize(value).Contains(normalizedExpected, StringComparison.Ordinal);
    }

    private static (string Name, string Label)? BestPhraseField(CandidateFields fields, string phrase)
    {
        if (fields.Tags.Contains(phrase, StringComparison.Ordinal))
        {
            return ("tags", "a tag");
        }

        if (fields.Keywords.Contains(phrase, StringComparison.Ordinal))
        {
            return ("keywords", "a keyword");
        }

        if (fields.ExtractedText.Contains(phrase, StringComparison.Ordinal))
        {
            return ("document text", "document text");
        }

        if (fields.ContentEntities.Contains(phrase, StringComparison.Ordinal))
        {
            return ("content entities", "a textual entity");
        }

        if (fields.ContentTopics.Contains(phrase, StringComparison.Ordinal))
        {
            return ("content topics", "a derived topic");
        }

        if (fields.ContentIntelligenceSummary.Contains(phrase, StringComparison.Ordinal))
        {
            return ("content summary", "the source-grounded summary");
        }

        if (fields.OcrText.Contains(phrase, StringComparison.Ordinal))
        {
            return ("OCR text", "OCR text");
        }

        if (fields.MediaTranscript.Contains(phrase, StringComparison.Ordinal))
        {
            return ("media transcript", "an audio or video transcript");
        }

        if (fields.MediaOcr.Contains(phrase, StringComparison.Ordinal))
        {
            return ("media OCR", "image or video-frame OCR");
        }

        if (fields.MediaMetadata.Contains(phrase, StringComparison.Ordinal))
        {
            return ("media metadata", "image, audio, or video metadata");
        }

        if (fields.MediaVisualDescription.Contains(phrase, StringComparison.Ordinal))
        {
            return ("visual description", "an optional visual description");
        }

        if (fields.Summary.Contains(phrase, StringComparison.Ordinal))
        {
            return ("summary", "the summary");
        }

        if (fields.Metadata.Contains(phrase, StringComparison.Ordinal))
        {
            return ("metadata", "metadata");
        }

        if (fields.Path.Contains(phrase, StringComparison.Ordinal))
        {
            return ("path", "the path");
        }

        return null;
    }

    private static double AddFieldMatch(
        string field,
        IReadOnlyList<string> tokens,
        ICollection<SearchRankingComponent> components,
        SearchRankingSignalKind kind,
        string fieldName,
        string label,
        double contribution,
        ref int rankClass)
    {
        var count = CountMatches(field, tokens);
        if (count == 0)
        {
            return 0;
        }

        rankClass = Math.Max(rankClass, 2);
        var matched = FirstMatchingToken(field, tokens);
        var explanation = kind switch
        {
            SearchRankingSignalKind.Tag => $"Matched tags: {matched}",
            SearchRankingSignalKind.ExtractedText => "Native text / document text matched",
            SearchRankingSignalKind.ContentTopic => $"Topic match: {matched}",
            SearchRankingSignalKind.ContentEntity => $"Entity match: {matched}",
            SearchRankingSignalKind.ContentIntelligenceSummary => "Source-grounded summary matched",
            _ => $"{label} matched",
        };
        return Add(
            components,
            kind,
            fieldName,
            contribution + (count - 1) * 10,
            explanation,
            matched);
    }

    private static double AddSmartTagMatches(
        IReadOnlyList<FileSmartTag> tags,
        IReadOnlyList<string> tokens,
        ICollection<SearchRankingComponent> components,
        ref int rankClass)
    {
        var contribution = 0d;
        foreach (var tag in tags.Where(tag =>
                     tag.Decision != SmartTagDecision.Rejected &&
                     (tag.State is SmartTagAssignmentState.Accepted or SmartTagAssignmentState.Automatic)))
        {
            var normalized = SearchTextNormalizer.Normalize(tag.Definition.DisplayName);
            if (!tokens.Any(token => normalized.Contains(token, StringComparison.Ordinal)))
            {
                continue;
            }

            rankClass = Math.Max(rankClass, 2);
            var kind = tag.Definition.Type switch
            {
                SmartTagType.Theme => SearchRankingSignalKind.SmartTagTheme,
                SmartTagType.DocumentType => SearchRankingSignalKind.SmartTagDocumentType,
                _ => SearchRankingSignalKind.SmartTagUser,
            };
            var label = tag.Definition.Type switch
            {
                SmartTagType.Theme => "Theme",
                SmartTagType.DocumentType => "Document Type",
                _ => "User Tag",
            };
            var authority = tag.State == SmartTagAssignmentState.Accepted
                ? tag.Definition.Type == SmartTagType.UserTag ? string.Empty : " — Accepted"
                : $" — {tag.Confidence}";
            var weight = tag.Definition.Type == SmartTagType.UserTag
                ? 180
                : tag.State == SmartTagAssignmentState.Accepted ? 165 : 145;
            contribution += Add(
                components,
                kind,
                $"smart tag {tag.Definition.Type}",
                weight,
                $"{label}: {tag.Definition.DisplayName}{authority}",
                tag.Definition.DisplayName);
        }

        return contribution;
    }

    private static double Add(
        ICollection<SearchRankingComponent> components,
        SearchRankingSignalKind kind,
        string field,
        double contribution,
        string explanation,
        string? matchedText)
    {
        components.Add(new SearchRankingComponent(kind, field, contribution, explanation, matchedText));
        return contribution;
    }

    private static int CountMatches(string field, IReadOnlyList<string> tokens) =>
        tokens.Count(token => field.Contains(token, StringComparison.Ordinal));

    private static string? FirstMatchingToken(string field, IReadOnlyList<string> tokens) =>
        tokens.FirstOrDefault(token => field.Contains(token, StringComparison.Ordinal));

    private static string? FindFuzzyFilenameMatch(
        IReadOnlyList<string> filenameTokens,
        IReadOnlyList<string> queryTokens)
    {
        foreach (var queryToken in queryTokens)
        {
            var maximumDistance = queryToken.Length >= 8 ? 2 : 1;
            var matched = filenameTokens.FirstOrDefault(
                filenameToken => WithinDistance(queryToken, filenameToken, maximumDistance));
            if (matched is null)
            {
                return null;
            }
        }

        return string.Join(' ', filenameTokens);
    }

    private static bool WithinDistance(string left, string right, int maximumDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maximumDistance ||
            left.Length > 64 ||
            right.Length > 64)
        {
            return false;
        }

        if (maximumDistance >= 1 && HasSingleAdjacentTransposition(left, right))
        {
            return true;
        }

        Span<int> previous = stackalloc int[65];
        Span<int> current = stackalloc int[65];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    substitution);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > maximumDistance)
            {
                return false;
            }

            var swap = previous;
            previous = current;
            current = swap;
        }

        return previous[right.Length] <= maximumDistance;
    }

    private static bool HasSingleAdjacentTransposition(string left, string right)
    {
        if (left.Length != right.Length || left.Length < 2)
        {
            return false;
        }

        var firstDifference = -1;
        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] == right[index])
            {
                continue;
            }

            if (firstDifference >= 0)
            {
                return index == firstDifference + 1 &&
                    left[firstDifference] == right[index] &&
                    left[index] == right[firstDifference] &&
                    left.AsSpan(index + 1).SequenceEqual(right.AsSpan(index + 1));
            }

            firstDifference = index;
        }

        return false;
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float>? right)
    {
        if (right is null || left.Count == 0 || left.Count != right.Count)
        {
            return 0;
        }

        double value = 0;
        for (var index = 0; index < left.Count; index++)
        {
            var product = left[index] * right[index];
            if (!double.IsFinite(product))
            {
                return 0;
            }

            value += product;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static string DescribeSimilarity(double similarity) => similarity switch
    {
        >= 0.75 => "strong",
        >= 0.45 => "moderate",
        _ => "limited",
    };

    private static void AddTieBreakComponents(
        SearchCandidateDocument candidate,
        ICollection<SearchRankingComponent> components)
    {
        if (candidate.ModifiedTimeUtc.HasValue)
        {
            components.Add(new SearchRankingComponent(
                SearchRankingSignalKind.Recency,
                "modified date",
                0,
                "Modification date used only as a tie-breaker"));
        }

        if (candidate.SourcePriority != 0)
        {
            components.Add(new SearchRankingComponent(
                SearchRankingSignalKind.SourcePriority,
                "source",
                candidate.SourcePriority,
                "Configured source priority used only as a tie-breaker"));
        }

        components.Add(new SearchRankingComponent(
            SearchRankingSignalKind.IndexingCompleteness,
            "coverage",
            candidate.IsFullyIndexed ? 1 : 0,
            candidate.IsFullyIndexed
                ? "File is fully indexed"
                : "File is only partially indexed"));
    }

    private sealed record CandidateFields(
        string FileName,
        string FileNameStem,
        IReadOnlyList<string> FileNameTokens,
        string FolderName,
        string Path,
        string Extension,
        string FileType,
        string Tags,
        string Keywords,
        string ContentTopics,
        string ContentEntities,
        string ContentIntelligenceSummary,
        string Metadata,
        string MediaMetadata,
        string MediaTranscript,
        string MediaOcr,
        string MediaVisualDescription,
        string ExtractedText,
        string OcrText,
        string Summary,
        string Chunks)
    {
        public static CandidateFields Create(SearchCandidateDocument candidate) => new(
            SearchTextNormalizer.Normalize(candidate.FileName),
            SearchTextNormalizer.Normalize(System.IO.Path.GetFileNameWithoutExtension(candidate.FileName)),
            SemanticTokenizer.Tokenize(SearchTextNormalizer.Normalize(candidate.FileName), 16),
            SearchTextNormalizer.Normalize(candidate.FolderName),
            SearchTextNormalizer.Normalize(string.Join(' ', candidate.RelativePath, candidate.FullPath)),
            SearchTextNormalizer.Normalize(candidate.Extension),
            SearchTextNormalizer.Normalize(candidate.FileType),
            SearchTextNormalizer.Normalize(string.Join(' ', candidate.Tags)),
            SearchTextNormalizer.Normalize(string.Join(' ', candidate.Keywords)),
            SearchTextNormalizer.Normalize(string.Join(' ', candidate.ContentIntelligence?.Topics.Select(item => item.DisplayName) ?? [])),
            SearchTextNormalizer.Normalize(string.Join(' ', candidate.ContentIntelligence?.Entities.Select(item => item.DisplayName) ?? [])),
            SearchTextNormalizer.Normalize(candidate.ContentIntelligence?.Summary?.Text),
            SearchTextNormalizer.Normalize(candidate.MetadataText),
            SearchTextNormalizer.Normalize(MediaEvidenceText.CreateMetadataText(candidate.MediaEvidence)),
            SearchTextNormalizer.Normalize(candidate.MediaEvidence?.Transcript),
            SearchTextNormalizer.Normalize(candidate.MediaEvidence?.OcrText),
            SearchTextNormalizer.Normalize(string.Join(' ', candidate.MediaEvidence?.VisualDescription, string.Join(' ', candidate.MediaEvidence?.VisualTags ?? []))),
            SearchTextNormalizer.Normalize(candidate.ExtractedText),
            SearchTextNormalizer.Normalize(candidate.OcrText),
            SearchTextNormalizer.Normalize(candidate.Summary),
            SearchTextNormalizer.Normalize(string.Join(' ', candidate.Chunks)));
    }

    private sealed record RankedValue(
        RankedSearchCandidate Result,
        int RankClass,
        double LiteralScore,
        double SemanticSimilarity);
}

/// <summary>Creates safe snippets from retained index fields without reading source files at query time.</summary>
public sealed class SearchSnippetFactory : ISearchSnippetFactory
{
    /// <inheritdoc />
    public SearchSnippet? Create(
        SearchCandidateDocument candidate,
        SearchInterpretation interpretation,
        IReadOnlyList<SearchRankingComponent> components)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(interpretation);
        ArgumentNullException.ThrowIfNull(components);
        var component = components.FirstOrDefault(item => item.Kind is
            SearchRankingSignalKind.ExtractedText or
            SearchRankingSignalKind.OcrText or
            SearchRankingSignalKind.MediaMetadata or
            SearchRankingSignalKind.MediaTranscript or
            SearchRankingSignalKind.MediaOcr or
            SearchRankingSignalKind.MediaVisualDescription or
            SearchRankingSignalKind.ContentTopic or
            SearchRankingSignalKind.ContentEntity or
            SearchRankingSignalKind.ContentIntelligenceSummary or
            SearchRankingSignalKind.Summary or
            SearchRankingSignalKind.Chunk or
            SearchRankingSignalKind.Metadata or
            SearchRankingSignalKind.Tag or
            SearchRankingSignalKind.Keyword or
            SearchRankingSignalKind.Path or
            SearchRankingSignalKind.ExactFilename or
            SearchRankingSignalKind.ExactFilenameStem or
            SearchRankingSignalKind.FilenamePrefix or
            SearchRankingSignalKind.FilenameSubstring or
            SearchRankingSignalKind.FilenameToken or
            SearchRankingSignalKind.FuzzyFilename);
        if (component is null)
        {
            return null;
        }

        var (source, label, text) = component.Kind switch
        {
            SearchRankingSignalKind.ExtractedText =>
                (SearchSnippetSource.ExtractedText, "Document text", candidate.ExtractedText),
            SearchRankingSignalKind.OcrText =>
                (SearchSnippetSource.OcrText, "OCR text", candidate.OcrText),
            SearchRankingSignalKind.MediaMetadata =>
                (SearchSnippetSource.MediaMetadata, "Media metadata", MediaEvidenceText.CreateMetadataText(candidate.MediaEvidence)),
            SearchRankingSignalKind.MediaTranscript =>
                (SearchSnippetSource.MediaTranscript, "Audio or video transcript", candidate.MediaEvidence?.Transcript),
            SearchRankingSignalKind.MediaOcr =>
                (SearchSnippetSource.MediaOcr, "Image or video OCR", candidate.MediaEvidence?.OcrText),
            SearchRankingSignalKind.MediaVisualDescription =>
                (SearchSnippetSource.MediaVisualDescription, "Optional visual description", candidate.MediaEvidence?.VisualDescription),
            SearchRankingSignalKind.ContentTopic =>
                (SearchSnippetSource.ContentTopic, "Derived topic", string.Join(", ", candidate.ContentIntelligence?.Topics.Select(item => item.DisplayName) ?? [])),
            SearchRankingSignalKind.ContentEntity =>
                (SearchSnippetSource.ContentEntity, "Textual entity", string.Join(", ", candidate.ContentIntelligence?.Entities.Select(item => item.DisplayName) ?? [])),
            SearchRankingSignalKind.ContentIntelligenceSummary =>
                (SearchSnippetSource.ContentIntelligenceSummary, "Source-grounded summary", candidate.ContentIntelligence?.Summary?.Text),
            SearchRankingSignalKind.Summary =>
                (SearchSnippetSource.Summary, "Summary", candidate.Summary),
            SearchRankingSignalKind.Chunk =>
                (SearchSnippetSource.Chunk, "Selected document text", candidate.Chunks.FirstOrDefault()),
            SearchRankingSignalKind.Metadata =>
                (SearchSnippetSource.Metadata, "Metadata", candidate.MetadataText),
            SearchRankingSignalKind.Tag or SearchRankingSignalKind.Keyword =>
                (SearchSnippetSource.TagOrKeyword, "Tag or keyword", string.Join(", ", candidate.Tags.Concat(candidate.Keywords))),
            SearchRankingSignalKind.Path =>
                (SearchSnippetSource.Path, "Path", candidate.RelativePath),
            _ => (SearchSnippetSource.Filename, "Filename", candidate.FileName),
        };
        var safe = Sanitize(text);
        if (safe.Length == 0)
        {
            return null;
        }

        var term = component.MatchedText ??
            interpretation.TopicTokens.FirstOrDefault(token =>
                safe.Contains(token, StringComparison.OrdinalIgnoreCase));
        var matchIndex = string.IsNullOrWhiteSpace(term)
            ? -1
            : safe.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        var start = matchIndex < 0
            ? 0
            : Math.Max(0, matchIndex - SearchLimits.MaximumSnippetCharacters / 3);
        var hasPrefix = start > 0;
        var available = SearchLimits.MaximumSnippetCharacters - (hasPrefix ? 1 : 0);
        var length = Math.Min(available, safe.Length - start);
        var hasSuffix = start + length < safe.Length;
        if (hasSuffix)
        {
            length = Math.Max(1, length - 1);
        }

        var bounded = safe.Substring(start, length);
        if (hasPrefix)
        {
            bounded = $"…{bounded}";
        }

        if (hasSuffix)
        {
            bounded = $"{bounded}…";
        }

        var highlights = new List<SearchHighlight>();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var localIndex = bounded.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (localIndex >= 0)
            {
                highlights.Add(new SearchHighlight(localIndex, Math.Min(term.Length, bounded.Length - localIndex)));
            }
        }

        return new SearchSnippet(
            source,
            label,
            bounded,
            Array.AsReadOnly(highlights.ToArray()));
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var output = new System.Text.StringBuilder(Math.Min(value.Length, SearchLimits.MaximumSnippetCharacters * 4));
        for (var index = 0; index < value.Length && output.Length < SearchLimits.MaximumSnippetCharacters * 4; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    output.Append(character).Append(value[++index]);
                }
                else
                {
                    output.Append('�');
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                output.Append('�');
            }
            else if (char.IsControl(character))
            {
                output.Append(' ');
            }
            else
            {
                output.Append(character);
            }
        }

        return string.Join(' ', output.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
