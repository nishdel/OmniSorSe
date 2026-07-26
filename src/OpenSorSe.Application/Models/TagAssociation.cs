namespace OpenSorSe.Application.Models;

/// <summary>
/// Identifies the source of an application-owned tag association.
/// </summary>
public enum TagSource
{
    /// <summary>The application derived the tag deterministically.</summary>
    Deterministic,
    /// <summary>An optional AI provider proposed the tag.</summary>
    AiSuggestion,
    /// <summary>The user explicitly accepted or created the tag.</summary>
    UserApproved,
    /// <summary>A local approved-pattern preference influenced the tag.</summary>
    Preference,
    /// <summary>The tag came from embedded document metadata.</summary>
    EmbeddedMetadata,
    /// <summary>The tag is a low-confidence candidate derived from local OCR text.</summary>
    OcrCandidate,
    /// <summary>The tag is a candidate derived from local semantic similarity.</summary>
    SemanticCandidate,
    /// <summary>The application confirmed the tag from a file extension or MIME type.</summary>
    FileType,
    /// <summary>The application confirmed the tag from a known document or filesystem date.</summary>
    Date,
    /// <summary>The tag was derived from the known containing-folder context.</summary>
    FolderContext,
}

/// <summary>
/// Identifies whether a tag is only suggested or is available to the current result-search session.
/// </summary>
public enum TagAcceptanceState
{
    /// <summary>The tag has not been accepted into the result session.</summary>
    Suggested,
    /// <summary>The tag was accepted for the current in-memory result session.</summary>
    Accepted,
    /// <summary>The tag was explicitly rejected.</summary>
    Rejected,
}

/// <summary>
/// Represents one normalized tag associated with a file in the current in-memory result session.
/// </summary>
public sealed record TagAssociation(
    string TagId,
    string FileId,
    string DisplayName,
    string NormalizedValue,
    string Category,
    TagSource Source,
    TagAcceptanceState AcceptanceState,
    string? Explanation,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>Gets the optional bounded confidence estimate for an inferred tag.</summary>
    public double? Confidence { get; init; }

    /// <summary>Gets the latest UTC state-update time when it differs from creation.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>Gets a bounded safe explanation of the source evidence.</summary>
    public string? ProvenanceDetails { get; init; }

    /// <summary>Gets the source fingerprint used to suppress rejected candidates until content changes.</summary>
    public string? SourceFingerprint { get; init; }

    /// <summary>Gets whether the application treats this as a confirmed non-editable system tag.</summary>
    public bool IsSystem { get; init; }
}
