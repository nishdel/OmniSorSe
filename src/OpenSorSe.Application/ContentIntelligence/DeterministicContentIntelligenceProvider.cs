using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.ContentIntelligence;

/// <summary>
/// Produces conservative local topics, textual entities, and an extractive summary.
/// It never opens files or calls a model; callers supply only already bounded index evidence.
/// </summary>
public sealed partial class DeterministicContentIntelligenceProvider : IContentIntelligenceProvider
{
    private static readonly HashSet<string> StopTerms = new(
        [
            "about", "after", "also", "and", "archive", "are", "audio", "before", "being", "between",
            "can", "could", "document", "file", "files", "for", "from", "had", "has", "have", "image",
            "into", "its", "meeting", "new", "notes", "not", "old", "photo", "recording", "scan", "screenshot",
            "that", "the", "their", "there", "these", "this", "through", "video", "was", "were", "will", "with",
            "you", "your", "aber", "auch", "das", "der", "die", "ein", "eine", "einer", "für", "ist", "mit",
            "nicht", "oder", "und", "von", "wird", "zu",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> OrganizationSuffixes = new(
        ["ag", "corp", "corporation", "gmbh", "inc", "limited", "llc", "ltd", "organisation", "organization"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> PlacePrepositions = new(
        ["at", "from", "in", "near", "to"],
        StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string Name => "local-deterministic-content";
    /// <inheritdoc />
    public string Version => "2.3.0";
    /// <inheritdoc />
    public ContentIntelligenceOrigin Origin => ContentIntelligenceOrigin.Deterministic;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<ContentIntelligenceResult> AnalyzeAsync(
        ContentIntelligenceRequest request,
        ContentIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (!settings.Enabled)
        {
            return Task.FromResult(new ContentIntelligenceResult(null, true, "Content Intelligence is disabled."));
        }

        var sources = NormalizeSources(request, settings);
        if (sources.Count == 0)
        {
            return Task.FromResult(new ContentIntelligenceResult(null, true, "No bounded indexed text was available."));
        }

        var topics = settings.TopicExtractionEnabled
            ? ExtractTopics(sources, settings, cancellationToken)
            : [];
        var entities = settings.EntityExtractionEnabled
            ? ExtractEntities(sources, settings, cancellationToken)
            : [];
        var summary = settings.SummaryGenerationEnabled
            ? CreateSummary(sources, topics, settings)
            : null;
        var keywords = topics
            .Select(topic => topic.NormalizedValue)
            .Concat(entities.Select(entity => entity.NormalizedValue))
            .Distinct(StringComparer.Ordinal)
            .Take(settings.MaximumKeywords)
            .ToArray();
        var fingerprintValue = string.Join(
            '|',
            Version,
            settings.TopicExtractionEnabled,
            settings.EntityExtractionEnabled,
            settings.SummaryGenerationEnabled,
            settings.MaximumInputCharacters,
            settings.MaximumTopics,
            settings.MaximumEntities,
            settings.MaximumKeywords,
            settings.MaximumSummaryCharacters,
            settings.MaximumEvidenceExcerptCharacters);
        var intelligence = new IndexedContentIntelligence
        {
            Topics = topics,
            Entities = entities,
            Keywords = Array.AsReadOnly(keywords),
            Summary = summary,
            Provider = Name,
            ProviderVersion = Version,
            ProcessingFingerprint = Hash(fingerprintValue),
        };
        return Task.FromResult(new ContentIntelligenceResult(
            intelligence,
            false,
            $"Derived {topics.Count} topics and {entities.Count} textual entities locally."));
    }

    private IReadOnlyList<ContentConcept> ExtractTopics(
        IReadOnlyList<BoundedSource> sources,
        ContentIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, TopicCandidate>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokens = Tokenize(source.Text);
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                if (!IsUsefulTerm(token.Normalized))
                {
                    continue;
                }

                AddTopic(candidates, token.Normalized, token.Display, source, SourceWeight(source.Kind), settings);
                if (index + 1 >= tokens.Count || !IsUsefulTerm(tokens[index + 1].Normalized))
                {
                    continue;
                }

                var next = tokens[index + 1];
                AddTopic(
                    candidates,
                    $"{token.Normalized} {next.Normalized}",
                    $"{token.Display} {next.Display}",
                    source,
                    SourceWeight(source.Kind) * (token.IsDistinctive || next.IsDistinctive ? 1.8 : 0.7),
                    settings);
            }
        }

        return candidates.Values
            .Where(candidate => !candidate.Normalized.Contains(' ') || candidate.Score >= 3 || candidate.Occurrences >= 2)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.SourceKinds.Count)
            .ThenBy(candidate => candidate.Normalized, StringComparer.Ordinal)
            .Take(settings.MaximumTopics)
            .Select(candidate => new ContentConcept
            {
                Kind = candidate.Normalized.Contains(' ') ? ContentConceptKind.Topic : ContentConceptKind.Keyword,
                DisplayName = Bound(candidate.Display, 96),
                NormalizedValue = candidate.Normalized,
                Confidence = Confidence(candidate.Occurrences, candidate.SourceKinds.Count, candidate.Score),
                Provider = Name,
                ProviderVersion = Version,
                Origin = Origin,
                Evidence = candidate.Evidence.Values.OrderBy(evidence => evidence.Source).Take(3).ToArray(),
            })
            .ToArray();
    }

    private IReadOnlyList<ContentConcept> ExtractEntities(
        IReadOnlyList<BoundedSource> sources,
        ContentIntelligenceSettings settings,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<(ContentConceptKind Kind, string Value), EntityCandidate>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (Match match in DatePattern().Matches(source.Text).Cast<Match>().Take(64))
            {
                AddEntity(candidates, ContentConceptKind.Date, match.Value, source, settings);
            }

            foreach (Match match in IdentifierPattern().Matches(source.Text).Cast<Match>().Take(64))
            {
                AddEntity(candidates, ContentConceptKind.Identifier, match.Value, source, settings);
            }

            foreach (Match match in NamedTermPattern().Matches(source.Text).Cast<Match>().Take(128))
            {
                var display = NormalizeWhitespace(match.Value);
                var normalized = SearchTextNormalizer.Normalize(display);
                if (normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(StopTerms.Contains))
                {
                    continue;
                }

                AddEntity(candidates, ClassifyNamedTerm(source.Text, match.Index, display, normalized), display, source, settings);
            }
        }

        return candidates.Values
            .OrderByDescending(candidate => candidate.SourceKinds.Count)
            .ThenByDescending(candidate => candidate.Occurrences)
            .ThenBy(candidate => candidate.Normalized, StringComparer.Ordinal)
            .Take(settings.MaximumEntities)
            .Select(candidate => new ContentConcept
            {
                Kind = candidate.Kind,
                DisplayName = Bound(candidate.Display, 128),
                NormalizedValue = candidate.Normalized,
                Confidence = candidate.SourceKinds.Count >= 2 || candidate.Occurrences >= 3
                    ? ContentIntelligenceConfidence.Strong
                    : candidate.Occurrences >= 2 ? ContentIntelligenceConfidence.Moderate : ContentIntelligenceConfidence.Limited,
                Provider = Name,
                ProviderVersion = Version,
                Origin = Origin,
                Evidence = candidate.Evidence.Values.OrderBy(evidence => evidence.Source).Take(3).ToArray(),
            })
            .ToArray();
    }

    private ContentSummaryEvidence? CreateSummary(
        IReadOnlyList<BoundedSource> sources,
        IReadOnlyList<ContentConcept> topics,
        ContentIntelligenceSettings settings)
    {
        var topicTerms = topics.Take(8).Select(topic => topic.NormalizedValue).ToArray();
        var candidate = sources
            .SelectMany(source => SentencePattern().Split(source.Text)
                .Select((sentence, index) => new { Source = source, Sentence = NormalizeWhitespace(sentence), Index = index }))
            .Where(item => item.Sentence.Length >= 24)
            .Select(item => new
            {
                item.Source,
                item.Sentence,
                Score = topicTerms.Count(topic => SearchTextNormalizer.Normalize(item.Sentence).Contains(topic, StringComparison.Ordinal)) * 4 +
                    SourceWeight(item.Source.Kind) - Math.Min(item.Index, 5) * 0.2,
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Source.Kind)
            .ThenBy(item => item.Sentence, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate is null)
        {
            return null;
        }

        return new ContentSummaryEvidence
        {
            Text = Bound(candidate.Sentence, settings.MaximumSummaryCharacters),
            Provider = Name,
            ProviderVersion = Version,
            Origin = Origin,
            Evidence = [Reference(candidate.Source, candidate.Sentence, settings)],
        };
    }

    private static IReadOnlyList<BoundedSource> NormalizeSources(ContentIntelligenceRequest request, ContentIntelligenceSettings settings)
    {
        var output = new List<BoundedSource>();
        var remaining = settings.MaximumInputCharacters;
        // The durable intelligence record is owned by the content hash and can
        // be reused by differently named duplicate files. Per-file names and
        // paths therefore remain Search signals and must not enter this shared
        // derived record.
        var supplied = (request.Sources ?? []).Take(32)
            .Select((source, index) => (Source: source, Index: index))
            .OrderBy(item => item.Source.Source)
            .ThenBy(item => item.Index);
        foreach (var item in supplied)
        {
            var source = item.Source;
            var safe = SanitizeBounded(source.Text, remaining);
            if (safe.Length == 0 || remaining <= 0)
            {
                continue;
            }

            output.Add(new BoundedSource(source.Source, safe));
            remaining -= safe.Length;
        }

        return output;
    }

    private static void AddTopic(
        IDictionary<string, TopicCandidate> candidates,
        string normalized,
        string display,
        BoundedSource source,
        double score,
        ContentIntelligenceSettings settings)
    {
        if (normalized.Length is < 3 or > 96)
        {
            return;
        }

        if (!candidates.TryGetValue(normalized, out var candidate))
        {
            candidate = new TopicCandidate(normalized, display);
            candidates.Add(normalized, candidate);
        }

        candidate.Occurrences++;
        candidate.Score += score;
        candidate.SourceKinds.Add(source.Kind);
        if (!candidate.Evidence.ContainsKey(source.Kind))
        {
            candidate.Evidence.Add(source.Kind, Reference(source, display, settings));
        }

        if (display.Any(char.IsUpper) && !candidate.Display.Any(char.IsUpper))
        {
            candidate.Display = display;
        }
    }

    private static void AddEntity(
        IDictionary<(ContentConceptKind Kind, string Value), EntityCandidate> candidates,
        ContentConceptKind kind,
        string display,
        BoundedSource source,
        ContentIntelligenceSettings settings)
    {
        var normalized = SearchTextNormalizer.Normalize(display);
        if (normalized.Length is < 3 or > 128 || StopTerms.Contains(normalized))
        {
            return;
        }

        var key = (kind, normalized);
        if (!candidates.TryGetValue(key, out var candidate))
        {
            candidate = new EntityCandidate(kind, normalized, display);
            candidates.Add(key, candidate);
        }

        candidate.Occurrences++;
        candidate.SourceKinds.Add(source.Kind);
        if (!candidate.Evidence.ContainsKey(source.Kind))
        {
            candidate.Evidence.Add(source.Kind, Reference(source, display, settings));
        }
    }

    private static ContentConceptKind ClassifyNamedTerm(string text, int index, string display, string normalized)
    {
        var terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Any(OrganizationSuffixes.Contains))
        {
            return ContentConceptKind.Organization;
        }

        var preceding = text[..Math.Max(0, index)].TrimEnd();
        var previousWord = Regex.Match(preceding, @"[\p{L}]+$", RegexOptions.CultureInvariant).Value;
        if (PlacePrepositions.Contains(previousWord))
        {
            return ContentConceptKind.Place;
        }

        if (display.Any(char.IsDigit) || display.Contains('-') ||
            display.Any(char.IsUpper) && display.Any(char.IsLower) && !display.Contains(' '))
        {
            return ContentConceptKind.ProductOrProject;
        }

        return ContentConceptKind.NamedTerm;
    }

    private static IReadOnlyList<Token> Tokenize(string value) => WordPattern().Matches(value)
        .Cast<Match>()
        .Take(4_096)
        .Select(match =>
        {
            var display = match.Value;
            var normalized = SearchTextNormalizer.Normalize(display);
            var distinctive = display.Length >= 2 &&
                (display.All(character => !char.IsLetter(character) || char.IsUpper(character)) || char.IsUpper(display[0]));
            return new Token(normalized, display, distinctive);
        })
        .Where(token => token.Normalized.Length > 0)
        .ToArray();

    private static ContentIntelligenceConfidence Confidence(int occurrences, int sources, double score) =>
        sources >= 2 || occurrences >= 4 || score >= 8
            ? ContentIntelligenceConfidence.Strong
            : occurrences >= 2 || score >= 3 ? ContentIntelligenceConfidence.Moderate : ContentIntelligenceConfidence.Limited;

    private static bool IsUsefulTerm(string value) =>
        value.Length is >= 3 and <= 64 && !StopTerms.Contains(value) && !value.All(char.IsDigit);

    private static double SourceWeight(ContentEvidenceSourceKind source) => source switch
    {
        ContentEvidenceSourceKind.Metadata => 1.5,
        ContentEvidenceSourceKind.VisualDescription => 0.5,
        _ => 1,
    };

    private static ContentEvidenceReference Reference(BoundedSource source, string evidence, ContentIntelligenceSettings? settings) => new(
        source.Kind,
        Hash($"{source.Kind}|{SearchTextNormalizer.Normalize(evidence)}"),
        Bound(NormalizeWhitespace(evidence), settings?.MaximumEvidenceExcerptCharacters ?? 160));

    private static string SanitizeBounded(string? value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || maximumCharacters <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumCharacters));
        for (var index = 0; index < value.Length && builder.Length < maximumCharacters; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]) &&
                    builder.Length + 2 <= maximumCharacters)
                {
                    builder.Append(character).Append(value[++index]);
                }
                else
                {
                    builder.Append('\uFFFD');
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                builder.Append('\uFFFD');
            }
            else
            {
                builder.Append(char.IsControl(character) ? ' ' : character);
            }
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{M}\p{N}_+.-]{1,63}", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\b(?:19|20)\d{2}[-/.](?:0?[1-9]|1[0-2])[-/.](?:0?[1-9]|[12]\d|3[01])\b", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b(?:invoice|inv|order|receipt|project|ticket|case)[\s_:#-]*[a-z0-9][a-z0-9._/-]{2,31}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"\b[\p{Lu}][\p{L}\p{M}\p{N}&+.-]{0,62}[\p{L}\p{M}\p{N}&+-](?:\s+[\p{Lu}][\p{L}\p{M}\p{N}&+.-]{0,62}[\p{L}\p{M}\p{N}&+-]){0,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex NamedTermPattern();

    [GeneratedRegex(@"(?:[.!?]+\s+)|(?:[\r\n]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SentencePattern();

    private sealed record BoundedSource(ContentEvidenceSourceKind Kind, string Text);
    private sealed record Token(string Normalized, string Display, bool IsDistinctive);

    private sealed class TopicCandidate(string normalized, string display)
    {
        public string Normalized { get; } = normalized;
        public string Display { get; set; } = display;
        public int Occurrences { get; set; }
        public double Score { get; set; }
        public HashSet<ContentEvidenceSourceKind> SourceKinds { get; } = [];
        public Dictionary<ContentEvidenceSourceKind, ContentEvidenceReference> Evidence { get; } = [];
    }

    private sealed class EntityCandidate(ContentConceptKind kind, string normalized, string display)
    {
        public ContentConceptKind Kind { get; } = kind;
        public string Normalized { get; } = normalized;
        public string Display { get; } = display;
        public int Occurrences { get; set; }
        public HashSet<ContentEvidenceSourceKind> SourceKinds { get; } = [];
        public Dictionary<ContentEvidenceSourceKind, ContentEvidenceReference> Evidence { get; } = [];
    }
}
