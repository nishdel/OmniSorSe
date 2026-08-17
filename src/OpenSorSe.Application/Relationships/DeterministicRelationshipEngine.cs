using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;

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
    public string Version => "3.0.0";

    /// <inheritdoc />
    public RelationshipFeatureSet CreateFeatures(RelationshipFileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);
        var stem = SearchTextNormalizer.Normalize(Path.GetFileNameWithoutExtension(document.FileName));
        var folder = string.Join(':', HashKey(document.SourceId), SearchTextNormalizer.Normalize(document.FolderName));
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
            BuildCandidateTerms(document)
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
            var candidateFeatures = candidate.PrecomputedRelationshipFeatures;
            if (candidateFeatures is null ||
                !string.Equals(candidateFeatures.FeatureVersion, Version, StringComparison.Ordinal))
            {
                candidateFeatures = CreateFeatures(candidate);
            }

            var proposal = Compare(
                target,
                targetFeatures,
                targetIdentifier,
                candidate,
                candidateFeatures,
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
        var scoring = new EvidenceAccumulator();
        string? strongContext = null;
        var exactContent = EqualNonEmpty(targetFeatures.ContentHash, candidateFeatures.ContentHash);
        if (exactContent)
        {
            scoring.Add(
                RelationshipEvidenceFamily.Identity,
                RelationshipEvidenceKind.DuplicateContent,
                HashKey(targetFeatures.ContentHash!),
                "Identical content fingerprint",
                8);
            strongContext = $"content:{HashKey(targetFeatures.ContentHash!)}";
        }

        if (EqualNonEmpty(targetIdentifier, candidateIdentifier))
        {
            var key = HashKey(targetIdentifier!);
            var targetKind = IdentifierEvidenceKind(target, targetIdentifier!);
            var candidateKind = IdentifierEvidenceKind(candidate, candidateIdentifier!);
            scoring.Add(
                RelationshipEvidenceFamily.Identity,
                targetKind == candidateKind ? targetKind : RelationshipEvidenceKind.Metadata,
                key,
                $"Same document identifier: {DisplayValue(targetIdentifier!)}",
                5);
            strongContext ??= $"identifier:{key}";
        }

        var stemOverlap = SharedTerms(targetFeatures.NormalizedStem, candidateFeatures.NormalizedStem);
        var stemSimilarity = Jaccard(targetFeatures.NormalizedStem, candidateFeatures.NormalizedStem);
        if (stemOverlap.Count >= 2 && stemSimilarity >= 0.6)
        {
            var display = string.Join(", ", stemOverlap.Take(3));
            scoring.Add(
                RelationshipEvidenceFamily.FilenameLexical,
                RelationshipEvidenceKind.Filename,
                HashKey(display),
                $"Shared filename terms: {display}",
                3);
            strongContext ??= $"name:{HashKey(display)}";
        }

        if (EqualNonEmpty(targetFeatures.ExtractedTextFingerprint, candidateFeatures.ExtractedTextFingerprint))
        {
            scoring.Add(
                RelationshipEvidenceFamily.ContentFingerprint,
                RelationshipEvidenceKind.ExtractedText,
                targetFeatures.ExtractedTextFingerprint!,
                "Matching extracted document text fingerprint",
                5);
            strongContext ??= $"text:{targetFeatures.ExtractedTextFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.OcrTextFingerprint, candidateFeatures.OcrTextFingerprint))
        {
            scoring.Add(
                RelationshipEvidenceFamily.ContentFingerprint,
                RelationshipEvidenceKind.OcrText,
                targetFeatures.OcrTextFingerprint!,
                "Matching OCR text fingerprint",
                5);
            strongContext ??= $"ocr:{targetFeatures.OcrTextFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.SummaryFingerprint, candidateFeatures.SummaryFingerprint))
        {
            var origin = target.ContentIntelligence?.Summary?.Origin == ContentIntelligenceOrigin.AiDerived ||
                candidate.ContentIntelligence?.Summary?.Origin == ContentIntelligenceOrigin.AiDerived
                    ? RelationshipEvidenceOrigin.AiDerived
                    : RelationshipEvidenceOrigin.Derived;
            scoring.Add(
                RelationshipEvidenceFamily.ContentFingerprint,
                RelationshipEvidenceKind.Summary,
                targetFeatures.SummaryFingerprint!,
                "Matching bounded summary fingerprint",
                origin == RelationshipEvidenceOrigin.AiDerived ? 2 : 3,
                origin);
            strongContext ??= $"summary:{targetFeatures.SummaryFingerprint}";
        }

        var hasStrongMediaFingerprint = false;
        if (EqualNonEmpty(targetFeatures.MediaTranscriptFingerprint, candidateFeatures.MediaTranscriptFingerprint))
        {
            scoring.Add(
                RelationshipEvidenceFamily.ContentFingerprint,
                RelationshipEvidenceKind.MediaTranscript,
                targetFeatures.MediaTranscriptFingerprint!,
                "Matching bounded local transcript fingerprint",
                5);
            hasStrongMediaFingerprint = true;
            strongContext ??= $"transcript:{targetFeatures.MediaTranscriptFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.MediaOcrFingerprint, candidateFeatures.MediaOcrFingerprint))
        {
            scoring.Add(
                RelationshipEvidenceFamily.ContentFingerprint,
                RelationshipEvidenceKind.MediaOcr,
                targetFeatures.MediaOcrFingerprint!,
                "Matching image or video-frame OCR fingerprint",
                5);
            hasStrongMediaFingerprint = true;
            strongContext ??= $"media-ocr:{targetFeatures.MediaOcrFingerprint}";
        }

        if (EqualNonEmpty(targetFeatures.MediaDeviceKey, candidateFeatures.MediaDeviceKey))
        {
            scoring.Add(
                RelationshipEvidenceFamily.StructuralMediaTemporal,
                RelationshipEvidenceKind.MediaMetadata,
                HashKey(targetFeatures.MediaDeviceKey!),
                "Same embedded camera or device metadata",
                1);
        }

        var sharedTags = SharedTags(target, candidate);
        if (sharedTags.Count > 0)
        {
            var display = string.Join(", ", sharedTags.Select(item => item.DisplayName).Take(3));
            var hasUserAuthority = sharedTags.Any(item =>
                item.Origin == SmartTagOrigin.User || item.State == SmartTagAssignmentState.Accepted);
            var origin = hasUserAuthority ? RelationshipEvidenceOrigin.User : RelationshipEvidenceOrigin.Deterministic;
            scoring.Add(
                RelationshipEvidenceFamily.TagAuthority,
                RelationshipEvidenceKind.Tag,
                HashKey(string.Join('|', sharedTags.Select(item => item.CanonicalKey))),
                $"Shared {RelationshipTagLabel(sharedTags)}: {display}",
                hasUserAuthority ? 3 : 2,
                origin);
            strongContext ??= $"tag:{HashKey(sharedTags[0].CanonicalKey)}";
        }

        var sharedTopics = SharedConcepts(
            target.ContentIntelligence?.Topics ?? [],
            candidate.ContentIntelligence?.Topics ?? []);
        var sharedEntities = SharedConcepts(
            target.ContentIntelligence?.Entities ?? [],
            candidate.ContentIntelligence?.Entities ?? []);
        var intelligenceTerms = sharedTopics.Select(item => item.NormalizedValue)
            .Concat(sharedEntities.Select(item => item.NormalizedValue))
            .ToHashSet(StringComparer.Ordinal);
        var sharedKeywords = SharedValues(target.Keywords, candidate.Keywords)
            .Where(value => !intelligenceTerms.Contains(value))
            .ToArray();
        if (sharedKeywords.Length > 0)
        {
            var display = string.Join(", ", sharedKeywords.Take(3));
            scoring.Add(
                RelationshipEvidenceFamily.FilenameLexical,
                RelationshipEvidenceKind.Keyword,
                HashKey(display),
                $"Shared keyword: {display}",
                sharedKeywords.Length >= 2 ? 3 : 2,
                RelationshipEvidenceOrigin.Derived);
            strongContext ??= $"keyword:{HashKey(sharedKeywords[0])}";
        }

        if (sharedTopics.Count >= 2)
        {
            var display = string.Join(", ", sharedTopics.Select(item => item.DisplayName).Take(3));
            var aiDerived = sharedTopics.Any(item => item.Origin == ContentIntelligenceOrigin.AiDerived);
            scoring.Add(
                RelationshipEvidenceFamily.NamedContext,
                RelationshipEvidenceKind.ContentTopic,
                HashKey(display),
                $"Shared content topics: {display}",
                aiDerived ? 2 : sharedTopics.Count >= 3 ? 5 : 4,
                aiDerived ? RelationshipEvidenceOrigin.AiDerived : RelationshipEvidenceOrigin.Derived);
            strongContext ??= $"topic:{HashKey(sharedTopics[0].NormalizedValue)}";
        }

        if (sharedEntities.Count > 0)
        {
            var display = string.Join(", ", sharedEntities.Select(item => $"{item.Kind}: {item.DisplayName}").Take(3));
            var aiDerived = sharedEntities.Any(item => item.Origin == ContentIntelligenceOrigin.AiDerived);
            scoring.Add(
                RelationshipEvidenceFamily.NamedContext,
                RelationshipEvidenceKind.ContentEntity,
                HashKey(display),
                $"Shared typed entity: {display}",
                aiDerived ? 2 : sharedEntities.Count >= 2 ? 4 : 3,
                aiDerived ? RelationshipEvidenceOrigin.AiDerived : RelationshipEvidenceOrigin.Derived);
            strongContext ??= $"entity:{HashKey(sharedEntities[0].NormalizedValue)}";
        }

        if (EqualNonEmpty(targetFeatures.FolderKey, candidateFeatures.FolderKey) &&
            !string.IsNullOrWhiteSpace(targetFeatures.FolderKey))
        {
            scoring.Add(
                RelationshipEvidenceFamily.StructuralMediaTemporal,
                RelationshipEvidenceKind.Folder,
                HashKey(targetFeatures.FolderKey),
                "Same source and indexed folder",
                1);
        }

        var similarity = Cosine(target.SemanticRepresentation, candidate.SemanticRepresentation);
        if (similarity >= 0.88 && scoring.IndependentFamilyCount > 0)
        {
            scoring.Add(
                RelationshipEvidenceFamily.SemanticCorroboration,
                RelationshipEvidenceKind.SemanticConcept,
                HashKey($"{target.FileId}|{candidate.FileId}"),
                "Related indexed concepts corroborate non-semantic evidence",
                similarity >= 0.94 ? 3 : 2,
                RelationshipEvidenceOrigin.Derived);
        }

        if (scoring.Score > 0 && TryGetTimeEvidence(target, candidate, TimeSpan.FromHours(2), out var timeExplanation))
        {
            scoring.Add(
                RelationshipEvidenceFamily.StructuralMediaTemporal,
                RelationshipEvidenceKind.Timestamp,
                "within-two-hours",
                timeExplanation,
                1);
        }

        // Preserve the established deterministic contract that two distinct, specific topics
        // corroborate one another even though the family cap prevents their raw scores stacking.
        // AI-derived topics never receive this exception.
        var hasCorroboratedDeterministicTopics = sharedTopics.Count >= 2 &&
            sharedTopics.All(item => item.Origin != ContentIntelligenceOrigin.AiDerived);
        var qualifies = exactContent || hasStrongMediaFingerprint ||
            hasCorroboratedDeterministicTopics ||
            scoring.Score >= 6 && scoring.IndependentNonStructuralFamilyCount >= 2;
        if (!qualifies)
        {
            return null;
        }

        var terms = targetFeatures.KeywordKeys
            .Intersect(candidateFeatures.KeywordKeys, StringComparer.Ordinal)
            .Concat(stemOverlap)
            .Concat(sharedTopics.Select(item => item.NormalizedValue))
            .Concat(sharedEntities.Select(item => item.NormalizedValue))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var type = InferType(exactContent, targetIdentifier, terms, target.FileName, candidate.FileName);
        var confidence = exactContent || scoring.Score >= 9 && scoring.IndependentFamilyCount >= 2
            ? RelationshipConfidence.High
            : hasCorroboratedDeterministicTopics && scoring.IndependentNonStructuralFamilyCount == 1
                ? RelationshipConfidence.Low
                : RelationshipConfidence.Medium;
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
            Evidence = scoring.Evidence,
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

    private static RelationshipType InferType(
        bool exactContent,
        string? identifier,
        IReadOnlyCollection<string> terms,
        string firstFileName,
        string secondFileName)
    {
        if (exactContent)
        {
            return RelationshipType.DocumentSet;
        }

        var all = terms
            .Concat(identifier is null ? [] : [SearchTextNormalizer.Normalize(identifier)])
            .Concat([
                SearchTextNormalizer.Normalize(Path.GetFileNameWithoutExtension(firstFileName)),
                SearchTextNormalizer.Normalize(Path.GetFileNameWithoutExtension(secondFileName)),
            ])
            .ToArray();
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
        if (!match.Success)
        {
            return null;
        }

        var tokens = SearchTextNormalizer.Normalize(match.Value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1 && tokens[0] is "invoice" or "inv" or "order" or "receipt" or "project" or "trip")
        {
            tokens = tokens[1..];
        }

        return tokens.Length == 0 ? null : string.Join(' ', tokens);
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

    private static IReadOnlyList<RelationshipTagEvidence> EffectiveTags(RelationshipFileDocument document)
    {
        if (document.TagEvidence.Count > 0)
        {
            return document.TagEvidence
                .Where(item => item.Decision != SmartTagDecision.Rejected)
                .Where(item =>
                    item.Origin == SmartTagOrigin.User ||
                    item.State == SmartTagAssignmentState.Accepted ||
                    item.Origin == SmartTagOrigin.DeterministicClassifier &&
                    item.Confidence == ContentIntelligenceConfidence.Strong &&
                    item.State == SmartTagAssignmentState.Automatic)
                .DistinctBy(item => item.CanonicalKey, StringComparer.Ordinal)
                .OrderBy(item => item.CanonicalKey, StringComparer.Ordinal)
                .Take(32)
                .ToArray();
        }

        return document.Tags
            .Select(SearchTextNormalizer.Normalize)
            .Where(item => item.Length is >= 3 and <= 64 && !GenericTerms.Contains(item))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(32)
            .Select(item => new RelationshipTagEvidence(
                item,
                item,
                SmartTagType.UserTag,
                ContentIntelligenceConfidence.Strong,
                SmartTagOrigin.User,
                SmartTagAssignmentState.Accepted,
                SmartTagDecision.Accepted))
            .ToArray();
    }

    private static IReadOnlyList<RelationshipTagEvidence> SharedTags(
        RelationshipFileDocument first,
        RelationshipFileDocument second)
    {
        var right = EffectiveTags(second).ToDictionary(item => item.CanonicalKey, StringComparer.Ordinal);
        return EffectiveTags(first)
            .Where(item => right.ContainsKey(item.CanonicalKey))
            .Select(item => MoreAuthoritativeTag(item, right[item.CanonicalKey]))
            .OrderBy(item => item.CanonicalKey, StringComparer.Ordinal)
            .Take(3)
            .ToArray();
    }

    private static RelationshipTagEvidence MoreAuthoritativeTag(
        RelationshipTagEvidence first,
        RelationshipTagEvidence second)
    {
        var firstPriority = TagPriority(first);
        var secondPriority = TagPriority(second);
        return firstPriority <= secondPriority ? first : second;
    }

    private static int TagPriority(RelationshipTagEvidence value) =>
        value.Origin == SmartTagOrigin.User || value.State == SmartTagAssignmentState.Accepted
            ? 0
            : value.Confidence == ContentIntelligenceConfidence.Strong ? 1 : 2;

    private static string RelationshipTagLabel(IReadOnlyList<RelationshipTagEvidence> tags) =>
        tags.Any(item => item.Type == SmartTagType.UserTag)
            ? "User Tag"
            : tags.Any(item => item.State == SmartTagAssignmentState.Accepted)
                ? "accepted Smart Tag"
                : "Strong generated Smart Tag";

    private static IReadOnlyList<SharedConcept> SharedConcepts(
        IReadOnlyList<ContentConcept> first,
        IReadOnlyList<ContentConcept> second)
    {
        var right = second
            .Where(IsUsableConcept)
            .GroupBy(ConceptKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return first
            .Where(IsUsableConcept)
            .Where(item => right.ContainsKey(ConceptKey(item)))
            .Select(item =>
            {
                var other = right[ConceptKey(item)];
                return new SharedConcept(
                    item.Kind,
                    item.DisplayName,
                    SearchTextNormalizer.Normalize(item.NormalizedValue),
                    item.Confidence <= other.Confidence ? item.Confidence : other.Confidence,
                    item.Origin == ContentIntelligenceOrigin.AiDerived || other.Origin == ContentIntelligenceOrigin.AiDerived
                        ? ContentIntelligenceOrigin.AiDerived
                        : ContentIntelligenceOrigin.Deterministic);
            })
            .DistinctBy(item => $"{item.Kind}:{item.NormalizedValue}", StringComparer.Ordinal)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.NormalizedValue, StringComparer.Ordinal)
            .Take(3)
            .ToArray();
    }

    private static bool IsUsableConcept(ContentConcept concept)
    {
        var normalized = SearchTextNormalizer.Normalize(concept.NormalizedValue);
        return normalized.Length is >= 3 and <= 64 && !GenericTerms.Contains(normalized);
    }

    private static string ConceptKey(ContentConcept concept) =>
        $"{concept.Kind}:{SearchTextNormalizer.Normalize(concept.NormalizedValue)}";

    private static IReadOnlyList<string> BuildCandidateTerms(RelationshipFileDocument document)
    {
        static bool Specific(string value) =>
            value.Length is >= 3 and <= 48 && !GenericTerms.Contains(value);

        var keywords = document.Keywords
            .Select(SearchTextNormalizer.Normalize)
            .Where(Specific)
            .Select(item => $"keyword:{item}");
        var tags = EffectiveTags(document)
            .Where(item => Specific(item.CanonicalKey))
            .Select(item => $"tag:{item.CanonicalKey}");
        var topics = document.ContentIntelligence?.Topics
            .Where(IsUsableConcept)
            .Select(item => $"topic:{SearchTextNormalizer.Normalize(item.NormalizedValue)}") ?? [];
        var entities = document.ContentIntelligence?.Entities
            .Where(IsUsableConcept)
            .Select(item => $"entity:{item.Kind}:{SearchTextNormalizer.Normalize(item.NormalizedValue)}") ?? [];
        return keywords.Concat(tags).Concat(topics).Concat(entities)
            .Where(item => item.Length <= 64)
            .ToArray();
    }

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

    private static bool TryGetTimeEvidence(
        RelationshipFileDocument first,
        RelationshipFileDocument second,
        TimeSpan maximum,
        out string explanation)
    {
        if (Within(first.MediaEvidence?.Metadata.CapturedAtUtc, second.MediaEvidence?.Metadata.CapturedAtUtc, maximum))
        {
            explanation = "Media capture timestamps are within two hours";
            return true;
        }

        if (Within(first.ModifiedTimeUtc, second.ModifiedTimeUtc, maximum))
        {
            explanation = "Filesystem modified timestamps are within two hours";
            return true;
        }

        if (Within(first.CreationTimeUtc, second.CreationTimeUtc, maximum))
        {
            explanation = "Filesystem created timestamps are within two hours";
            return true;
        }

        explanation = string.Empty;
        return false;
    }

    private static bool Within(DateTimeOffset? first, DateTimeOffset? second, TimeSpan maximum)
    {
        var left = ValidTimestamp(first);
        var right = ValidTimestamp(second);
        return left.HasValue && right.HasValue && (left.Value - right.Value).Duration() <= maximum;
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
            document.Keywords.Count > 256 || document.Tags.Count > 256 || document.TagEvidence.Count > 256 ||
            SearchTextNormalizer.ContainsMalformedUnicode(document.FileName))
        {
            throw new InvalidDataException("The indexed relationship document is malformed or exceeds supported bounds.");
        }
    }

    [GeneratedRegex(@"\b(?:(?:invoice|inv|order|receipt|project|trip)[\s_\-:#]*)?[a-z]{0,6}\d{3,}(?:[._\-/]\d+)*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex DocumentIdentifierPattern();

    [GeneratedRegex(@"\bv?\d+(?:\.\d+){1,3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex VersionPattern();

    private sealed record SharedConcept(
        ContentConceptKind Kind,
        string DisplayName,
        string NormalizedValue,
        ContentIntelligenceConfidence Confidence,
        ContentIntelligenceOrigin Origin);

    private sealed class EvidenceAccumulator
    {
        private static readonly IReadOnlyDictionary<RelationshipEvidenceFamily, int> FamilyCaps =
            new Dictionary<RelationshipEvidenceFamily, int>
            {
                [RelationshipEvidenceFamily.Identity] = 8,
                [RelationshipEvidenceFamily.ContentFingerprint] = 5,
                [RelationshipEvidenceFamily.NamedContext] = 5,
                [RelationshipEvidenceFamily.FilenameLexical] = 3,
                [RelationshipEvidenceFamily.TagAuthority] = 3,
                [RelationshipEvidenceFamily.StructuralMediaTemporal] = 2,
                [RelationshipEvidenceFamily.SemanticCorroboration] = 3,
                [RelationshipEvidenceFamily.UserAuthority] = 8,
            };
        private readonly Dictionary<RelationshipEvidenceFamily, int> _scores = [];
        private readonly List<(RelationshipEvidence Evidence, int Points)> _evidence = [];

        public int Score => _scores.Values.Sum();

        public int IndependentFamilyCount => _scores.Count(item =>
            item.Value > 0 && item.Key != RelationshipEvidenceFamily.SemanticCorroboration);

        public int IndependentNonStructuralFamilyCount => _scores.Count(item =>
            item.Value > 0 && item.Key is not (
                RelationshipEvidenceFamily.SemanticCorroboration or
                RelationshipEvidenceFamily.StructuralMediaTemporal));

        public IReadOnlyList<RelationshipEvidence> Evidence => _evidence
            .DistinctBy(item => (item.Evidence.Kind, item.Evidence.EvidenceKey))
            .OrderByDescending(item => item.Points)
            .ThenBy(item => item.Evidence.Family)
            .ThenBy(item => item.Evidence.Kind)
            .ThenBy(item => item.Evidence.EvidenceKey, StringComparer.Ordinal)
            .Take(RelationshipLimits.MaximumEvidencePerRelationship)
            .Select(item => item.Evidence)
            .ToArray();

        public void Add(
            RelationshipEvidenceFamily family,
            RelationshipEvidenceKind kind,
            string key,
            string explanation,
            int points,
            RelationshipEvidenceOrigin origin = RelationshipEvidenceOrigin.Deterministic)
        {
            var cap = FamilyCaps[family];
            var current = _scores.GetValueOrDefault(family);
            _scores[family] = Math.Min(cap, current + Math.Clamp(points, 0, cap));
            var familyToken = family switch
            {
                RelationshipEvidenceFamily.Identity => "identity",
                RelationshipEvidenceFamily.ContentFingerprint => "content",
                RelationshipEvidenceFamily.NamedContext => "context",
                RelationshipEvidenceFamily.FilenameLexical => "lexical",
                RelationshipEvidenceFamily.TagAuthority => "tag",
                RelationshipEvidenceFamily.StructuralMediaTemporal => "structural",
                RelationshipEvidenceFamily.SemanticCorroboration => "semantic",
                _ => "user",
            };
            var originToken = origin switch
            {
                RelationshipEvidenceOrigin.AiDerived => "ai",
                RelationshipEvidenceOrigin.Derived => "derived",
                RelationshipEvidenceOrigin.User => "user",
                _ => "deterministic",
            };
            _evidence.Add((
                new RelationshipEvidence(
                    kind,
                    Bound($"{familyToken}:{originToken}:{key}", 128),
                    Bound(explanation, RelationshipLimits.MaximumEvidenceTextCharacters)),
                points));
        }
    }
}
