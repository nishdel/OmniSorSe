using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Application.Relationships;

/// <summary>Discovers bounded relationships from deterministic indexed evidence.</summary>
public sealed partial class DeterministicRelationshipEngine : IRelationshipEngine
{
    private static readonly HashSet<string> GenericTerms = new(
        [
            "and", "archive", "copy", "document", "file", "final", "from", "image", "new", "old",
            "photo", "scan", "the", "this", "version", "with",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> TripTerms = new(
        ["booking", "dolomites", "expense", "flight", "gpx", "holiday", "hotel", "packing", "ticket", "travel", "trip"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> PurchaseTerms = new(
        ["invoice", "manual", "order", "payment", "purchase", "receipt", "warranty"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ProjectTerms = new(
        ["build", "design", "milestone", "monitoring", "project", "release", "roadmap", "specification"],
        StringComparer.Ordinal);

    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the deterministic relationship engine.</summary>
    public DeterministicRelationshipEngine(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Algorithm => "deterministic-evidence";

    /// <inheritdoc />
    public string Version => "2.2.0";

    /// <inheritdoc />
    public RelationshipFeatureSet CreateFeatures(RelationshipFileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);
        var stem = SearchTextNormalizer.Normalize(Path.GetFileNameWithoutExtension(document.FileName));
        var folder = SearchTextNormalizer.Normalize(document.FolderName);
        var date = ValidTimestamp(document.MediaEvidence?.Metadata.CapturedAtUtc) ??
            ValidTimestamp(document.ModifiedTimeUtc) ??
            ValidTimestamp(document.CreationTimeUtc);
        var features = new RelationshipFeatureSet(
            document.FileId,
            Bound(stem, 256),
            Bound(folder, 512),
            BoundOrNull(document.ContentHash, 128),
            date?.UtcDateTime.Date.Ticks,
            Fingerprint(document.ExtractedText),
            Fingerprint(document.OcrText),
            Fingerprint(document.Summary),
            document.Keywords
                .Concat(document.Tags)
                .Select(SearchTextNormalizer.Normalize)
                .Where(term => term.Length is >= 3 and <= 64 && !GenericTerms.Contains(term))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(term => term, StringComparer.Ordinal)
                .Take(32)
                .ToArray(),
            Version)
        {
            MediaTranscriptFingerprint = Fingerprint(document.MediaEvidence?.Transcript),
            MediaOcrFingerprint = Fingerprint(document.MediaEvidence?.OcrText),
            MediaDeviceKey = DeviceKey(document.MediaEvidence),
            CaptureDateBucket = ValidTimestamp(document.MediaEvidence?.Metadata.CapturedAtUtc)?.UtcDateTime.Date.Ticks,
        };
        return features;
    }

    /// <inheritdoc />
    public IReadOnlyList<RelationshipProposal> Discover(
        RelationshipFileDocument target,
        IReadOnlyList<RelationshipFileDocument> candidates,
        int maximumRelationships,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateDocument(target);
        if (maximumRelationships is < 1 or > RelationshipLimits.MaximumRelationshipsPerFile)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRelationships));
        }

        if (target.RelationshipAnalysisSuppressed)
        {
            return [];
        }

        var targetFeatures = CreateFeatures(target);
        var targetIdentifier = ExtractIdentifier(target);
        var proposals = new List<RelationshipProposal>(Math.Min(maximumRelationships, candidates.Count));
        foreach (var candidate in candidates
                     .Where(item => !string.Equals(item.FileId, target.FileId, StringComparison.Ordinal))
                     .OrderBy(item => item.FileId, StringComparer.Ordinal)
                     .Take(RelationshipLimits.MaximumCandidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.RelationshipAnalysisSuppressed)
            {
                continue;
            }

            ValidateDocument(candidate);
            var proposal = Compare(
                target,
                targetFeatures,
                targetIdentifier,
                candidate,
                CreateFeatures(candidate),
                ExtractIdentifier(candidate));
            if (proposal is not null)
            {
                proposals.Add(proposal);
            }
        }

        return proposals
            .OrderByDescending(item => item.Relationship.Confidence)
            .ThenBy(item => item.Relationship.Type)
            .ThenBy(item => item.Relationship.Id, StringComparer.Ordinal)
            .Take(maximumRelationships)
            .ToArray();
    }

    private RelationshipProposal? Compare(
        RelationshipFileDocument target,
        RelationshipFeatureSet targetFeatures,
        string? targetIdentifier,
        RelationshipFileDocument candidate,
        RelationshipFeatureSet candidateFeatures,
        string? candidateIdentifier)
    {
        var evidence = new List<RelationshipEvidence>(RelationshipLimits.MaximumEvidencePerRelationship);
        var score = 0;
        string? strongContext = null;
        var exactContent = EqualNonEmpty(targetFeatures.ContentHash, candidateFeatures.ContentHash);
        if (exactContent)
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.DuplicateContent, HashKey(targetFeatures.ContentHash!), "Identical content fingerprint"));
            score += 8;
            strongContext = $"content:{HashKey(targetFeatures.ContentHash!)}";
        }

        if (EqualNonEmpty(targetIdentifier, candidateIdentifier))
        {
            var key = HashKey(targetIdentifier!);
            var targetKind = IdentifierEvidenceKind(target, targetIdentifier!);
            var candidateKind = IdentifierEvidenceKind(candidate, candidateIdentifier!);
            evidence.Add(Evidence(
                targetKind == candidateKind ? targetKind : RelationshipEvidenceKind.Metadata,
                key,
                $"Same document identifier: {DisplayValue(targetIdentifier!)}"));
            score += 5;
            strongContext ??= $"identifier:{key}";
        }

        var stemOverlap = SharedTerms(targetFeatures.NormalizedStem, candidateFeatures.NormalizedStem);
        var stemSimilarity = Jaccard(targetFeatures.NormalizedStem, candidateFeatures.NormalizedStem);
        if (stemOverlap.Count >= 2 && stemSimilarity >= 0.6)
        {
            var display = string.Join(", ", stemOverlap.Take(3));
            evidence.Add(Evidence(RelationshipEvidenceKind.Filename, HashKey(display), $"Shared filename terms: {display}"));
            score += 3;
            strongContext ??= $"name:{HashKey(display)}";
        }

        if (EqualNonEmpty(targetFeatures.ExtractedTextFingerprint, candidateFeatures.ExtractedTextFingerprint))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.ExtractedText, targetFeatures.ExtractedTextFingerprint!, "Matching extracted document text fingerprint"));
            score += 5;
            strongContext ??= $"text:{targetFeatures.ExtractedTextFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.OcrTextFingerprint, candidateFeatures.OcrTextFingerprint))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.OcrText, targetFeatures.OcrTextFingerprint!, "Matching OCR text fingerprint"));
            score += 5;
            strongContext ??= $"ocr:{targetFeatures.OcrTextFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.SummaryFingerprint, candidateFeatures.SummaryFingerprint))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.Summary, targetFeatures.SummaryFingerprint!, "Matching bounded summary fingerprint"));
            score += 3;
            strongContext ??= $"summary:{targetFeatures.SummaryFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.MediaTranscriptFingerprint, candidateFeatures.MediaTranscriptFingerprint))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.MediaTranscript, targetFeatures.MediaTranscriptFingerprint!, "Matching bounded local transcript fingerprint"));
            score += 5;
            strongContext ??= $"transcript:{targetFeatures.MediaTranscriptFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.MediaOcrFingerprint, candidateFeatures.MediaOcrFingerprint))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.MediaOcr, targetFeatures.MediaOcrFingerprint!, "Matching image or video-frame OCR fingerprint"));
            score += 5;
            strongContext ??= $"media-ocr:{targetFeatures.MediaOcrFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.MediaDeviceKey, candidateFeatures.MediaDeviceKey))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.MediaMetadata, HashKey(targetFeatures.MediaDeviceKey!), "Same embedded camera or device metadata"));
            score += 1;
        }

        var sharedTags = SharedValues(target.Tags, candidate.Tags);
        if (sharedTags.Count > 0)
        {
            var display = string.Join(", ", sharedTags.Take(3));
            evidence.Add(Evidence(RelationshipEvidenceKind.Tag, HashKey(display), $"Shared tag: {display}"));
            score += 2;
            strongContext ??= $"tag:{HashKey(sharedTags[0])}";
        }

        var sharedKeywords = SharedValues(target.Keywords, candidate.Keywords);
        if (sharedKeywords.Count > 0)
        {
            var display = string.Join(", ", sharedKeywords.Take(3));
            evidence.Add(Evidence(RelationshipEvidenceKind.Keyword, HashKey(display), $"Shared keyword: {display}"));
            score += sharedKeywords.Count >= 2 ? 3 : 2;
            strongContext ??= $"keyword:{HashKey(sharedKeywords[0])}";
        }

        if (EqualNonEmpty(targetFeatures.FolderKey, candidateFeatures.FolderKey) &&
            !string.IsNullOrWhiteSpace(targetFeatures.FolderKey))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.Folder, HashKey(targetFeatures.FolderKey), "Same indexed folder"));
            score += 1;
        }

        var similarity = Cosine(target.SemanticRepresentation, candidate.SemanticRepresentation);
        if (similarity >= 0.88 && (sharedKeywords.Count > 0 || sharedTags.Count > 0 || stemOverlap.Count > 0))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.SemanticConcept, HashKey($"{target.FileId}|{candidate.FileId}"), "Related indexed concepts corroborate literal evidence"));
            score += similarity >= 0.94 ? 3 : 2;
        }

        if (score > 0 && CloseInTime(target, candidate, TimeSpan.FromHours(2)))
        {
            evidence.Add(Evidence(RelationshipEvidenceKind.Timestamp, "within-two-hours", "Indexed timestamps are within two hours"));
            score += 1;
        }

        evidence = evidence
            .DistinctBy(item => (item.Kind, item.EvidenceKey))
            .Take(RelationshipLimits.MaximumEvidencePerRelationship)
            .ToList();
        var hasStrongSingleEvidence = exactContent || evidence.Any(item => item.Kind is
            RelationshipEvidenceKind.ExtractedText or
            RelationshipEvidenceKind.OcrText or
            RelationshipEvidenceKind.MediaTranscript or
            RelationshipEvidenceKind.MediaOcr);
        if (score < 4 || evidence.Count < 2 && !hasStrongSingleEvidence)
        {
            return null;
        }

        var terms = targetFeatures.KeywordKeys
            .Intersect(candidateFeatures.KeywordKeys, StringComparer.Ordinal)
            .Concat(stemOverlap)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var type = InferType(exactContent, targetIdentifier, terms);
        var confidence = score >= 8 ? RelationshipConfidence.High :
            score >= 5 || evidence.Any(item => item.Kind == RelationshipEvidenceKind.SemanticConcept) && evidence.Count >= 2
                ? RelationshipConfidence.Medium
                : RelationshipConfidence.Low;
        var now = _timeProvider.GetUtcNow();
        var first = string.CompareOrdinal(target.FileId, candidate.FileId) <= 0 ? target.FileId : candidate.FileId;
        var second = string.CompareOrdinal(target.FileId, candidate.FileId) <= 0 ? candidate.FileId : target.FileId;
        var id = "rel:" + HashKey($"{first}|{second}|{type}");
        var relationship = new FileRelationship
        {
            Id = id,
            FirstFileId = first,
            SecondFileId = second,
            Type = type,
            Confidence = confidence,
            Evidence = evidence.AsReadOnly(),
            Algorithm = Algorithm,
            AlgorithmVersion = Version,
            CreatedAtUtc = now,
            LastValidatedAtUtc = now,
        };
        SmartCollectionSuggestion? collection = null;
        if (confidence >= RelationshipConfidence.Medium && strongContext is not null)
        {
            var title = CreateCollectionTitle(type, targetIdentifier, terms, target, candidate);
            var contextKey = $"{type}:{strongContext}";
            collection = new SmartCollectionSuggestion(
                contextKey,
                title,
                $"Files grouped by {type.ToString().ToLowerInvariant()} evidence.",
                relationship.Explanation,
                type,
                confidence,
                first,
                second,
                id);
        }

        return new RelationshipProposal(relationship, collection);
    }

    private static RelationshipType InferType(bool exactContent, string? identifier, IReadOnlyCollection<string> terms)
    {
        if (exactContent)
        {
            return RelationshipType.DocumentSet;
        }

        var all = terms.Concat(identifier is null ? [] : [SearchTextNormalizer.Normalize(identifier)]).ToArray();
        if (all.Any(term => PurchaseTerms.Any(value => term.Contains(value, StringComparison.Ordinal))))
        {
            return RelationshipType.SamePurchase;
        }

        if (all.Any(term => TripTerms.Any(value => term.Contains(value, StringComparison.Ordinal))))
        {
            return RelationshipType.SameTrip;
        }

        if (identifier is not null && VersionPattern().IsMatch(identifier))
        {
            return RelationshipType.SameVersion;
        }

        return all.Any(term => ProjectTerms.Any(value => term.Contains(value, StringComparison.Ordinal)))
            ? RelationshipType.SameProject
            : RelationshipType.SameTopic;
    }

    private static string CreateCollectionTitle(
        RelationshipType type,
        string? identifier,
        IReadOnlyList<string> terms,
        RelationshipFileDocument target,
        RelationshipFileDocument candidate)
    {
        var core = identifier ?? terms.FirstOrDefault() ??
            SharedTerms(
                    SearchTextNormalizer.Normalize(Path.GetFileNameWithoutExtension(target.FileName)),
                    SearchTextNormalizer.Normalize(Path.GetFileNameWithoutExtension(candidate.FileName)))
                .FirstOrDefault() ?? type.ToString();
        core = string.Join(' ', core.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6));
        var suffix = type switch
        {
            RelationshipType.SamePurchase => "Purchase",
            RelationshipType.SameTrip => "Trip",
            RelationshipType.SameProject => "Project",
            RelationshipType.SameVersion => "Versions",
            RelationshipType.DocumentSet => "Document set",
            _ => "Collection",
        };
        var title = char.ToUpperInvariant(core[0]) + core[1..] + " " + suffix;
        return Bound(title, RelationshipLimits.MaximumCollectionTitleCharacters);
    }

    private static string? ExtractIdentifier(RelationshipFileDocument document)
    {
        var input = Bound(string.Join(' ', document.FileName, document.MetadataText, document.Summary), 4_096);
        var match = DocumentIdentifierPattern().Match(input);
        return match.Success ? SearchTextNormalizer.Normalize(match.Value) : null;
    }

    private static RelationshipEvidenceKind IdentifierEvidenceKind(
        RelationshipFileDocument document,
        string identifier)
    {
        if (SearchTextNormalizer.Normalize(document.FileName).Contains(identifier, StringComparison.Ordinal))
        {
            return RelationshipEvidenceKind.Filename;
        }

        if (SearchTextNormalizer.Normalize(document.MetadataText).Contains(identifier, StringComparison.Ordinal))
        {
            return RelationshipEvidenceKind.Metadata;
        }

        return RelationshipEvidenceKind.Summary;
    }

    private static IReadOnlyList<string> SharedValues(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        first.Select(SearchTextNormalizer.Normalize)
            .Where(value => value.Length is >= 3 and <= 64 && !GenericTerms.Contains(value))
            .Intersect(
                second.Select(SearchTextNormalizer.Normalize)
                    .Where(value => value.Length is >= 3 and <= 64 && !GenericTerms.Contains(value)),
                StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(3)
            .ToArray();

    private static IReadOnlyList<string> SharedTerms(string first, string second)
    {
        var firstTerms = first.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3 && !GenericTerms.Contains(term));
        return firstTerms.Intersect(
                second.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(term => term.Length >= 3 && !GenericTerms.Contains(term)),
                StringComparer.Ordinal)
            .OrderBy(term => term, StringComparer.Ordinal)
            .ToArray();
    }

    private static double Jaccard(string first, string second)
    {
        var left = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var right = second.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        return left.Intersect(right).Count() / (double)left.Union(right).Count();
    }

    private static double Cosine(IReadOnlyList<float>? first, IReadOnlyList<float>? second)
    {
        if (first is null || second is null || first.Count == 0 || first.Count != second.Count || first.Count > 4_096)
        {
            return 0;
        }

        double dot = 0;
        double left = 0;
        double right = 0;
        for (var index = 0; index < first.Count; index++)
        {
            if (!float.IsFinite(first[index]) || !float.IsFinite(second[index]))
            {
                return 0;
            }

            dot += first[index] * second[index];
            left += first[index] * first[index];
            right += second[index] * second[index];
        }

        return left <= 0 || right <= 0 ? 0 : Math.Clamp(dot / Math.Sqrt(left * right), -1, 1);
    }

    private static bool CloseInTime(RelationshipFileDocument first, RelationshipFileDocument second, TimeSpan maximum)
    {
        var firstTime = ValidTimestamp(first.MediaEvidence?.Metadata.CapturedAtUtc) ?? ValidTimestamp(first.ModifiedTimeUtc) ?? ValidTimestamp(first.CreationTimeUtc);
        var secondTime = ValidTimestamp(second.MediaEvidence?.Metadata.CapturedAtUtc) ?? ValidTimestamp(second.ModifiedTimeUtc) ?? ValidTimestamp(second.CreationTimeUtc);
        return firstTime.HasValue && secondTime.HasValue && (firstTime.Value - secondTime.Value).Duration() <= maximum;
    }

    private static DateTimeOffset? ValidTimestamp(DateTimeOffset? value) =>
        value is { Year: >= 1601 and <= 9998 } ? value : null;

    private static string? Fingerprint(string? value)
    {
        var normalized = SearchTextNormalizer.Normalize(value);
        if (normalized.Length < 16)
        {
            return null;
        }

        return HashKey(Bound(normalized, 32_768));
    }

    private static string? DeviceKey(OpenSorSe.Application.Media.IndexedMediaEvidence? evidence)
    {
        var value = SearchTextNormalizer.Normalize(string.Join(' ', evidence?.Metadata.DeviceMake, evidence?.Metadata.DeviceModel));
        return value.Length is >= 3 and <= 256 ? value : null;
    }

    private static RelationshipEvidence Evidence(RelationshipEvidenceKind kind, string key, string explanation) =>
        new(kind, Bound(key, 128), Bound(explanation, RelationshipLimits.MaximumEvidenceTextCharacters));

    private static string HashKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    private static string DisplayValue(string value) => Bound(value, 80);

    private static bool EqualNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) && string.Equals(first, second, StringComparison.Ordinal);

    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private static string? BoundOrNull(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value.Trim(), maximum);

    private static void ValidateDocument(RelationshipFileDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.FileId) || document.FileId.Length > 128 ||
            string.IsNullOrWhiteSpace(document.SourceId) || document.SourceId.Length > 128 ||
            string.IsNullOrWhiteSpace(document.FileName) || document.FileName.Length > 1_024 ||
            document.Keywords.Count > 256 || document.Tags.Count > 256 ||
            SearchTextNormalizer.ContainsMalformedUnicode(document.FileName))
        {
            throw new InvalidDataException("The indexed relationship document is malformed or exceeds supported bounds.");
        }
    }

    [GeneratedRegex(@"\b(?:(?:invoice|inv|order|receipt|project|trip)[\s_\-:#]*)?[a-z]{0,6}\d{3,}(?:[._\-/]\d+)*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex DocumentIdentifierPattern();

    [GeneratedRegex(@"\bv?\d+(?:\.\d+){1,3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex VersionPattern();
}
