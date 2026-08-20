using System.Globalization;
using System.Text;
using OpenSorSe.Application.ContentIntelligence;

namespace OpenSorSe.Application.SmartTags;

/// <summary>
/// Assigns bounded taxonomy candidates from grouped local evidence. Derived topics and summaries
/// are deliberately not counted again when the underlying native/OCR/transcript family matched.
/// </summary>
public sealed class DeterministicSmartTagClassifier : ISmartTagClassifier
{
    private const double ModerateThreshold = 2.0;
    private const double StrongThreshold = 5.0;
    private const double DocumentConflictMargin = 0.75;
    private readonly SmartTagTaxonomy _taxonomy;

    /// <summary>Initializes the classifier with the validated built-in taxonomy.</summary>
    public DeterministicSmartTagClassifier(SmartTagTaxonomy taxonomy)
    {
        _taxonomy = taxonomy ?? throw new ArgumentNullException(nameof(taxonomy));
    }

    /// <inheritdoc />
    public string Name => "OmniSorSe deterministic Smart Tags";

    /// <inheritdoc />
    public string Version => $"1.0-taxonomy-{_taxonomy.Version}";

    /// <inheritdoc />
    public Task<SmartTagClassificationResult> ClassifyAsync(
        SmartTagClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);

        var families = CreateFamilies(request);
        var hasSemanticEvidence = families.Any(item => !item.IsWeak && item.Text.Length > 0);
        if (!hasSemanticEvidence)
        {
            return Task.FromResult(Result(request,
                SmartTagClassificationState.NoEvidence,
                [],
                "No supported content evidence was available for local classification."));
        }

        var candidates = new List<SmartTagCandidate>();
        foreach (var definition in _taxonomy.Definitions.Where(item => !item.IsHidden))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scored = Score(definition, families);
            if (scored.Score <= 0)
            {
                continue;
            }

            var confidence = scored.HasStrongStructuralMatch ||
                scored.Score >= StrongThreshold && scored.IndependentFamilyCount >= 2
                    ? ContentIntelligenceConfidence.Strong
                    : scored.Score >= ModerateThreshold
                        ? ContentIntelligenceConfidence.Moderate
                        : ContentIntelligenceConfidence.Limited;
            candidates.Add(new SmartTagCandidate
            {
                TagId = definition.TagId,
                Type = definition.Type,
                Confidence = confidence,
                EvidenceScore = Math.Round(scored.Score, 3, MidpointRounding.AwayFromZero),
                Origin = SmartTagOrigin.DeterministicClassifier,
                Classifier = Name,
                ClassifierVersion = Version,
                TaxonomyVersion = _taxonomy.Version,
                InputFingerprint = request.InputFingerprint,
                Evidence = Array.AsReadOnly(scored.Evidence
                    .Take(SmartTagLimits.MaximumEvidencePerAssignment)
                    .ToArray()),
            });
        }

        var visibleThemes = candidates
            .Where(item => item.Type == SmartTagType.Theme && item.Confidence != ContentIntelligenceConfidence.Limited)
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.EvidenceScore)
            .ThenBy(item => item.TagId, StringComparer.Ordinal)
            .Take(SmartTagLimits.MaximumThemesPerFile)
            .ToArray();
        var documentTypes = candidates
            .Where(item => item.Type == SmartTagType.DocumentType && item.Confidence != ContentIntelligenceConfidence.Limited)
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.EvidenceScore)
            .ThenBy(item => item.TagId, StringComparer.Ordinal)
            .ToArray();

        var conflict = documentTypes.Length > 1 &&
            documentTypes[0].Confidence == documentTypes[1].Confidence &&
            documentTypes[0].EvidenceScore - documentTypes[1].EvidenceScore < DocumentConflictMargin;
        var visibleDocuments = conflict ? [] : documentTypes.Take(1).ToArray();
        var visible = visibleThemes.Concat(visibleDocuments).ToArray();
        if (visible.Length > 0)
        {
            return Task.FromResult(Result(request,
                SmartTagClassificationState.Classified,
                Array.AsReadOnly(visible),
                "Local evidence produced bounded explainable classifications."));
        }

        return Task.FromResult(Result(request,
            conflict ? SmartTagClassificationState.ConflictingEvidence : SmartTagClassificationState.InsufficientEvidence,
            [],
            conflict
                ? "Conflicting document-type evidence was retained as an unresolved classification state."
                : "Available evidence did not meet the visible Smart Tag suggestion threshold."));
    }

    private SmartTagClassificationResult Result(
        SmartTagClassificationRequest request,
        SmartTagClassificationState state,
        IReadOnlyList<SmartTagCandidate> candidates,
        string message) => new(state, candidates, message)
        {
            Classifier = Name,
            ClassifierVersion = Version,
            TaxonomyVersion = _taxonomy.Version,
            InputFingerprint = request.InputFingerprint,
        };

    private static void ValidateRequest(SmartTagClassificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileId) || request.FileId.Length > 256 ||
            string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Length > 512 ||
            request.RelativePath.Length > 4096 ||
            string.IsNullOrWhiteSpace(request.InputFingerprint) || request.InputFingerprint.Length > 256)
        {
            throw new ArgumentException("The Smart Tag classification request is invalid or exceeds its bounds.", nameof(request));
        }
    }

    private static IReadOnlyList<EvidenceFamily> CreateFamilies(SmartTagClassificationRequest request)
    {
        var derived = string.Join(' ',
            (request.ContentIntelligence?.Topics.Select(item => item.DisplayName) ?? [])
                .Concat(request.ContentIntelligence?.Keywords ?? [])
                .Append(request.ContentIntelligence?.Summary?.Text ?? string.Empty));
        return
        [
            new("native", ContentEvidenceSourceKind.ExtractedText, Normalize(request.ExtractedText), 1.2, 3.2, 4.5, false),
            new("ocr", ContentEvidenceSourceKind.OcrText, Normalize(request.OcrText), 1.0, 2.6, 3.8, false),
            new("transcript", ContentEvidenceSourceKind.MediaTranscript, Normalize(request.Transcript), 1.0, 2.6, 3.8, false),
            new("media-ocr", ContentEvidenceSourceKind.MediaOcr, Normalize(request.MediaOcrText), 0.9, 2.3, 3.5, false),
            new("metadata", ContentEvidenceSourceKind.Metadata, Normalize(request.MetadataText), 1.1, 2.8, 4.0, false),
            new("derived", ContentEvidenceSourceKind.ExtractedText, Normalize(derived), 0.45, 0.9, 1.0, true),
            new("filename", ContentEvidenceSourceKind.Metadata, Normalize(Path.GetFileNameWithoutExtension(request.FileName)), 0.45, 1.0, 1.1, true),
            new("path", ContentEvidenceSourceKind.Metadata, Normalize(Path.GetDirectoryName(request.RelativePath)), 0.2, 0.4, 0.5, true),
        ];
    }

    private static ScoredCandidate Score(SmartTagDefinition definition, IReadOnlyList<EvidenceFamily> families)
    {
        var evidence = new List<SmartTagEvidence>();
        var score = 0d;
        var independent = 0;
        var strong = false;
        var directFamilyMatched = false;
        foreach (var family in families.Where(item => !item.IsWeak))
        {
            var match = Match(definition, family);
            if (match.Score <= 0)
            {
                continue;
            }

            directFamilyMatched = true;
            score += match.Score;
            independent++;
            strong |= match.Strong;
            evidence.Add(match.Evidence!);
        }

        foreach (var family in families.Where(item => item.IsWeak))
        {
            if (family.Name == "derived" && directFamilyMatched)
            {
                continue;
            }

            var match = Match(definition, family);
            if (match.Score <= 0)
            {
                continue;
            }

            score += match.Score;
            evidence.Add(match.Evidence!);
        }

        return new ScoredCandidate(
            score,
            independent,
            strong,
            evidence.OrderBy(item => item.EvidenceKey, StringComparer.Ordinal).ToArray());
    }

    private static FamilyMatch Match(SmartTagDefinition definition, EvidenceFamily family)
    {
        if (family.Text.Length == 0)
        {
            return default;
        }

        var strongPhrase = definition.StrongPhrases.FirstOrDefault(value => ContainsPhrase(family.Text, value));
        if (strongPhrase is not null)
        {
            return new FamilyMatch(
                family.StrongPhraseWeight,
                !family.IsWeak,
                Evidence(family, strongPhrase, highSpecificity: true));
        }

        var matches = definition.Aliases.Where(value => ContainsPhrase(family.Text, value)).Distinct(StringComparer.Ordinal).ToArray();
        if (matches.Length == 0)
        {
            return default;
        }

        return new FamilyMatch(
            Math.Min(family.MaximumWeight, matches.Length * family.AliasWeight),
            false,
            Evidence(family, matches[0], highSpecificity: false));
    }

    private static SmartTagEvidence Evidence(EvidenceFamily family, string phrase, bool highSpecificity) => new(
        family.Source,
        string.Create(CultureInfo.InvariantCulture, $"{family.Name}:{phrase.Replace(' ', '-')}"),
        string.Create(CultureInfo.InvariantCulture,
            $"{DisplayFamily(family.Name)} matched the known {(highSpecificity ? "high-specificity phrase" : "taxonomy term")} “{phrase}”."));

    private static string DisplayFamily(string family) => family switch
    {
        "native" => "Native document text",
        "ocr" => "Document OCR",
        "transcript" => "Local transcript",
        "media-ocr" => "Media OCR",
        "metadata" => "Embedded or media metadata",
        "derived" => "Existing Content Intelligence",
        "filename" => "Filename supporting evidence",
        _ => "Folder supporting evidence",
    };

    private static bool ContainsPhrase(string normalizedText, string phrase)
    {
        var start = 0;
        while (start < normalizedText.Length)
        {
            var index = normalizedText.IndexOf(phrase, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var end = index + phrase.Length;
            if ((index == 0 || normalizedText[index - 1] == ' ') &&
                (end == normalizedText.Length || normalizedText[end] == ' '))
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length + 2, 65_538));
        builder.Append(' ');
        var previousSpace = true;
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (builder.Length >= 65_537)
            {
                break;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }

        if (!previousSpace)
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private sealed record EvidenceFamily(
        string Name,
        ContentEvidenceSourceKind Source,
        string Text,
        double AliasWeight,
        double MaximumWeight,
        double StrongPhraseWeight,
        bool IsWeak);

    private readonly record struct FamilyMatch(double Score, bool Strong, SmartTagEvidence? Evidence);

    private sealed record ScoredCandidate(
        double Score,
        int IndependentFamilyCount,
        bool HasStrongStructuralMatch,
        IReadOnlyList<SmartTagEvidence> Evidence);
}
