using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.ContentIntelligence;

/// <summary>Identifies retained source evidence without exposing provider-specific storage.</summary>
public enum ContentEvidenceSourceKind
{
    /// <summary>Deterministic filesystem or embedded metadata supplied the evidence.</summary>
    Metadata,
    /// <summary>Native document text supplied the evidence.</summary>
    ExtractedText,
    /// <summary>Document OCR supplied the evidence.</summary>
    OcrText,
    /// <summary>A bounded local media transcript supplied the evidence.</summary>
    MediaTranscript,
    /// <summary>Image or representative-frame OCR supplied the evidence.</summary>
    MediaOcr,
    /// <summary>An explicitly enabled derived visual description supplied the evidence.</summary>
    VisualDescription,
}

/// <summary>Distinguishes deterministic evidence from optional model-derived evidence.</summary>
public enum ContentIntelligenceOrigin
{
    /// <summary>The value was produced by local deterministic logic.</summary>
    Deterministic,
    /// <summary>The value was produced by an explicitly configured AI provider.</summary>
    AiDerived,
}

/// <summary>Uses understandable levels instead of fabricated percentages.</summary>
public enum ContentIntelligenceConfidence
{
    /// <summary>One bounded evidence item supports the value.</summary>
    Limited,
    /// <summary>Repeated or corroborated bounded evidence supports the value.</summary>
    Moderate,
    /// <summary>Several independent or strongly weighted evidence items support the value.</summary>
    Strong,
}

/// <summary>Identifies one bounded content concept.</summary>
public enum ContentConceptKind
{
    /// <summary>A normalized multi-word subject.</summary>
    Topic,
    /// <summary>A normalized single-word subject.</summary>
    Keyword,
    /// <summary>A person's name found in indexed text, not a biometric identity.</summary>
    Person,
    /// <summary>An organization name found in indexed text.</summary>
    Organization,
    /// <summary>A place name found in indexed text.</summary>
    Place,
    /// <summary>A product or project name found in indexed text.</summary>
    ProductOrProject,
    /// <summary>A textual date found in indexed text.</summary>
    Date,
    /// <summary>A bounded document-specific identifier found in indexed text.</summary>
    Identifier,
    /// <summary>A conservative named term whose narrower category is uncertain.</summary>
    NamedTerm,
}

/// <summary>References the exact bounded source used for one derived value.</summary>
public sealed record ContentEvidenceReference(
    ContentEvidenceSourceKind Source,
    string EvidenceKey,
    string Excerpt);

/// <summary>Contains one bounded normalized concept with provider provenance.</summary>
public sealed record ContentConcept
{
    /// <summary>Gets the concept category.</summary>
    public required ContentConceptKind Kind { get; init; }
    /// <summary>Gets the bounded display value.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets the deterministic normalized matching value.</summary>
    public required string NormalizedValue { get; init; }
    /// <summary>Gets an understandable evidence-strength level.</summary>
    public required ContentIntelligenceConfidence Confidence { get; init; }
    /// <summary>Gets the provider identity.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets the provider algorithm version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets whether the value was deterministic or AI-derived.</summary>
    public required ContentIntelligenceOrigin Origin { get; init; }
    /// <summary>Gets the bounded retained evidence references.</summary>
    public IReadOnlyList<ContentEvidenceReference> Evidence { get; init; } = [];
}

/// <summary>Contains one short source-grounded summary with provider provenance.</summary>
public sealed record ContentSummaryEvidence
{
    /// <summary>Gets the bounded source-grounded summary.</summary>
    public required string Text { get; init; }
    /// <summary>Gets the provider identity.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets the provider algorithm version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets whether the summary was deterministic or AI-derived.</summary>
    public required ContentIntelligenceOrigin Origin { get; init; }
    /// <summary>Gets the exact bounded evidence references used by the summary.</summary>
    public IReadOnlyList<ContentEvidenceReference> Evidence { get; init; } = [];
}

/// <summary>Contains durable bounded content intelligence for one content fingerprint.</summary>
public sealed record IndexedContentIntelligence
{
    /// <summary>Gets bounded normalized subjects.</summary>
    public IReadOnlyList<ContentConcept> Topics { get; init; } = [];
    /// <summary>Gets bounded textual named entities.</summary>
    public IReadOnlyList<ContentConcept> Entities { get; init; } = [];
    /// <summary>Gets bounded normalized Search keywords.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
    /// <summary>Gets an optional source-grounded summary.</summary>
    public ContentSummaryEvidence? Summary { get; init; }
    /// <summary>Gets the provider identity.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets the provider algorithm version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets the settings/provider fingerprint used for cache invalidation.</summary>
    public required string ProcessingFingerprint { get; init; }
}

/// <summary>Contains one named bounded source field supplied to a provider.</summary>
public sealed record ContentIntelligenceSourceText(ContentEvidenceSourceKind Source, string Text);

/// <summary>
/// Contains bounded known content-hash-owned evidence. Filename and path data
/// are deliberately absent because they remain per-file Search signals.
/// </summary>
public sealed record ContentIntelligenceRequest(IReadOnlyList<ContentIntelligenceSourceText> Sources);

/// <summary>Contains one isolated provider result.</summary>
public sealed record ContentIntelligenceResult(
    IndexedContentIntelligence? Intelligence,
    bool WasSkipped,
    string Message);

/// <summary>Extracts bounded content concepts without coupling indexing to one implementation.</summary>
public interface IContentIntelligenceProvider
{
    /// <summary>Gets the stable provider identity.</summary>
    string Name { get; }
    /// <summary>Gets the stable provider algorithm version.</summary>
    string Version { get; }
    /// <summary>Gets whether the provider is deterministic or AI-derived.</summary>
    ContentIntelligenceOrigin Origin { get; }
    /// <summary>Reports whether this optional provider can currently process work.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    /// <summary>Analyzes only caller-supplied bounded index evidence.</summary>
    Task<ContentIntelligenceResult> AnalyzeAsync(
        ContentIntelligenceRequest request,
        ContentIntelligenceSettings settings,
        CancellationToken cancellationToken);
}
