using OpenSorSe.Application.ContentIntelligence;
using System.Text;

namespace OpenSorSe.Application.SmartTags;

/// <summary>Identifies a durable Smart Tag class. Facets such as entities and dates remain separate.</summary>
public enum SmartTagType
{
    /// <summary>A bounded subject classification such as Finance or Travel.</summary>
    Theme,
    /// <summary>A bounded structural classification such as Invoice or Contract.</summary>
    DocumentType,
    /// <summary>An explicit freeform local label owned by the user.</summary>
    UserTag,
}

/// <summary>Identifies where a durable tag definition or assignment originated.</summary>
public enum SmartTagOrigin
{
    /// <summary>The definition is part of the versioned built-in taxonomy.</summary>
    BuiltInTaxonomy,
    /// <summary>Local deterministic evidence produced the assignment.</summary>
    DeterministicClassifier,
    /// <summary>An explicitly configured optional model suggested the assignment.</summary>
    AiSuggestion,
    /// <summary>The user created the tag or explicitly established the assignment.</summary>
    User,
}

/// <summary>Records explicit user authority separately from regenerated classifier output.</summary>
public enum SmartTagDecision
{
    /// <summary>No explicit user decision exists.</summary>
    None,
    /// <summary>The user accepted the generated classification.</summary>
    Accepted,
    /// <summary>The user rejected the generated classification.</summary>
    Rejected,
}

/// <summary>Describes how a currently visible assignment became active.</summary>
public enum SmartTagAssignmentState
{
    /// <summary>The generated classification is awaiting review.</summary>
    Suggested,
    /// <summary>A Strong deterministic result is active without being converted to user authority.</summary>
    Automatic,
    /// <summary>The assignment is explicitly user-created or accepted.</summary>
    Accepted,
}

/// <summary>Explains whether classification was possible without pretending missing evidence is a semantic result.</summary>
public enum SmartTagClassificationState
{
    /// <summary>No semantic evidence was available.</summary>
    NoEvidence,
    /// <summary>Evidence existed but did not meet the visible suggestion threshold.</summary>
    InsufficientEvidence,
    /// <summary>Mutually exclusive candidates could not be resolved safely.</summary>
    ConflictingEvidence,
    /// <summary>At least one bounded visible classification was produced.</summary>
    Classified,
}

/// <summary>Defines one stable taxonomy or user-created tag identity.</summary>
public sealed record SmartTagDefinition
{
    /// <summary>Gets the stable language-neutral identity.</summary>
    public required string TagId { get; init; }
    /// <summary>Gets the semantic class.</summary>
    public required SmartTagType Type { get; init; }
    /// <summary>Gets the stable canonical key independent of display localization.</summary>
    public required string CanonicalKey { get; init; }
    /// <summary>Gets the localized bounded display label.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets the optional stable parent identity.</summary>
    public string? ParentTagId { get; init; }
    /// <summary>Gets the taxonomy version or user-definition version.</summary>
    public required string TaxonomyVersion { get; init; }
    /// <summary>Gets the definition origin.</summary>
    public required SmartTagOrigin Origin { get; init; }
    /// <summary>Gets whether OmniSorSe supplied this definition.</summary>
    public bool IsBuiltIn { get; init; }
    /// <summary>Gets whether the user hid the built-in definition from ordinary suggestions.</summary>
    public bool IsHidden { get; init; }
    /// <summary>Gets bounded normalized aliases used only by local deterministic classification.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
    /// <summary>Gets bounded high-specificity phrases that may establish strong structural evidence.</summary>
    public IReadOnlyList<string> StrongPhrases { get; init; } = [];
}

/// <summary>Contains one bounded, local explanation without retaining arbitrary source content.</summary>
public sealed record SmartTagEvidence(
    ContentEvidenceSourceKind Source,
    string EvidenceKey,
    string Explanation);

/// <summary>Contains one regenerated classifier candidate.</summary>
public sealed record SmartTagCandidate
{
    /// <summary>Gets the known taxonomy identity.</summary>
    public required string TagId { get; init; }
    /// <summary>Gets the tag class.</summary>
    public required SmartTagType Type { get; init; }
    /// <summary>Gets an understandable evidence-strength band, not a probability.</summary>
    public required ContentIntelligenceConfidence Confidence { get; init; }
    /// <summary>Gets a bounded internal evidence score used only for deterministic ordering and tests.</summary>
    public required double EvidenceScore { get; init; }
    /// <summary>Gets the assignment origin.</summary>
    public required SmartTagOrigin Origin { get; init; }
    /// <summary>Gets the classifier identity.</summary>
    public required string Classifier { get; init; }
    /// <summary>Gets the classifier version.</summary>
    public required string ClassifierVersion { get; init; }
    /// <summary>Gets the taxonomy version.</summary>
    public required string TaxonomyVersion { get; init; }
    /// <summary>Gets the relevant input fingerprint.</summary>
    public required string InputFingerprint { get; init; }
    /// <summary>Gets at most a few strongest evidence reasons.</summary>
    public IReadOnlyList<SmartTagEvidence> Evidence { get; init; } = [];
}

/// <summary>Contains one complete bounded classification pass.</summary>
public sealed record SmartTagClassificationResult(
    SmartTagClassificationState State,
    IReadOnlyList<SmartTagCandidate> Candidates,
    string Message)
{
    /// <summary>Gets the classifier identity even when no visible candidate was produced.</summary>
    public required string Classifier { get; init; }
    /// <summary>Gets the classifier version even when no visible candidate was produced.</summary>
    public required string ClassifierVersion { get; init; }
    /// <summary>Gets the taxonomy version used for the complete pass.</summary>
    public required string TaxonomyVersion { get; init; }
    /// <summary>Gets the relevant bounded input fingerprint for the complete pass.</summary>
    public required string InputFingerprint { get; init; }
}

/// <summary>Supplies already-indexed bounded evidence to a classifier.</summary>
public sealed record SmartTagClassificationRequest
{
    /// <summary>Gets the durable file identity.</summary>
    public required string FileId { get; init; }
    /// <summary>Gets the filename as supporting rather than authoritative semantic evidence.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets the source-relative path as weak supporting evidence.</summary>
    public required string RelativePath { get; init; }
    /// <summary>Gets bounded native text already retained by indexing.</summary>
    public string? ExtractedText { get; init; }
    /// <summary>Gets bounded OCR text already retained by indexing.</summary>
    public string? OcrText { get; init; }
    /// <summary>Gets bounded local transcript text already retained by Media Intelligence.</summary>
    public string? Transcript { get; init; }
    /// <summary>Gets bounded media OCR already retained by Media Intelligence.</summary>
    public string? MediaOcrText { get; init; }
    /// <summary>Gets bounded metadata text already retained by indexing.</summary>
    public string? MetadataText { get; init; }
    /// <summary>Gets existing bounded topics, entities, keywords, and summary evidence.</summary>
    public IndexedContentIntelligence? ContentIntelligence { get; init; }
    /// <summary>Gets the relevant source/content fingerprint.</summary>
    public required string InputFingerprint { get; init; }
}

/// <summary>Projects one durable effective assignment for Search and user review.</summary>
public sealed record FileSmartTag
{
    /// <summary>Gets the durable file identifier.</summary>
    public required string FileId { get; init; }
    /// <summary>Gets the durable definition.</summary>
    public required SmartTagDefinition Definition { get; init; }
    /// <summary>Gets the classifier confidence, or Strong for explicit user tags.</summary>
    public required ContentIntelligenceConfidence Confidence { get; init; }
    /// <summary>Gets the assignment origin.</summary>
    public required SmartTagOrigin Origin { get; init; }
    /// <summary>Gets the visible assignment state after user authority is applied.</summary>
    public required SmartTagAssignmentState State { get; init; }
    /// <summary>Gets the explicit decision when one exists.</summary>
    public required SmartTagDecision Decision { get; init; }
    /// <summary>Gets bounded retained evidence.</summary>
    public IReadOnlyList<SmartTagEvidence> Evidence { get; init; } = [];
    /// <summary>Gets when the durable association last changed.</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>Bounds all persisted and displayed Smart Tag data.</summary>
public static class SmartTagLimits
{
    /// <summary>Maximum generated themes retained per file.</summary>
    public const int MaximumThemesPerFile = 3;
    /// <summary>Maximum document-type candidates retained per file.</summary>
    public const int MaximumDocumentTypesPerFile = 2;
    /// <summary>Maximum explicit user tags per file.</summary>
    public const int MaximumUserTagsPerFile = 12;
    /// <summary>Maximum bounded evidence reasons per assignment.</summary>
    public const int MaximumEvidencePerAssignment = 3;
    /// <summary>Maximum user-facing tag label length.</summary>
    public const int MaximumDisplayNameCharacters = 64;
    /// <summary>Maximum taxonomy definitions accepted from one resource.</summary>
    public const int MaximumTaxonomyDefinitions = 64;
}

/// <summary>Validates and normalizes bounded local user-tag input consistently across UI and storage.</summary>
public static class SmartTagUserInput
{
    /// <summary>Normalizes a display label while retaining the user's meaningful characters.</summary>
    public static string NormalizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A user tag cannot be empty.", nameof(value));
        }

        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length is < 1 or > SmartTagLimits.MaximumDisplayNameCharacters ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A user tag must contain at most {SmartTagLimits.MaximumDisplayNameCharacters} safe characters.",
                nameof(value));
        }

        return normalized;
    }

    /// <summary>Creates a stable language-preserving canonical key for a validated display label.</summary>
    public static string NormalizeCanonicalKey(string value)
    {
        var display = NormalizeDisplayName(value);
        var builder = new StringBuilder(display.Length);
        var previousSeparator = false;
        foreach (var character in display.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousSeparator = false;
            }
            else if (!previousSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousSeparator = true;
            }
        }

        var normalized = builder.ToString().Trim('-');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A user tag must contain at least one letter or number.", nameof(value));
        }

        return normalized;
    }
}

/// <summary>Classifies already-known bounded evidence without owning persistence or extraction.</summary>
public interface ISmartTagClassifier
{
    /// <summary>Gets the stable classifier identity.</summary>
    string Name { get; }
    /// <summary>Gets the algorithm version used in processing fingerprints.</summary>
    string Version { get; }
    /// <summary>Classifies one known file cooperatively.</summary>
    Task<SmartTagClassificationResult> ClassifyAsync(
        SmartTagClassificationRequest request,
        CancellationToken cancellationToken = default);
}
