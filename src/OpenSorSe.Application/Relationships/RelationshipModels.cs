using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.ContentIntelligence;

namespace OpenSorSe.Application.Relationships;

/// <summary>Defines defensive bounds for relationship discovery, graphs, and presentation.</summary>
public static class RelationshipLimits
{
    /// <summary>Gets the maximum candidate documents examined for one incremental analysis.</summary>
    public const int MaximumCandidates = 512;

    /// <summary>Gets the maximum relationships retained for one analyzed file.</summary>
    public const int MaximumRelationshipsPerFile = 128;

    /// <summary>Gets the maximum evidence items retained for one relationship.</summary>
    public const int MaximumEvidencePerRelationship = 8;

    /// <summary>Gets the maximum members returned or retained for one collection.</summary>
    public const int MaximumCollectionMembers = 2_000;

    /// <summary>Gets the maximum direct contextual Search expansions.</summary>
    public const int MaximumSearchExpansions = 100;

    /// <summary>Gets the maximum bounded display text retained in one evidence value.</summary>
    public const int MaximumEvidenceTextCharacters = 256;

    /// <summary>Gets the maximum collection title length.</summary>
    public const int MaximumCollectionTitleCharacters = 120;

    /// <summary>Gets the maximum collection description length.</summary>
    public const int MaximumCollectionDescriptionCharacters = 512;
}

/// <summary>Identifies an explainable relationship category.</summary>
public enum RelationshipType
{
    /// <summary>The files contain evidence of the same project.</summary>
    SameProject,
    /// <summary>The files contain evidence of the same trip.</summary>
    SameTrip,
    /// <summary>The files contain evidence of the same purchase.</summary>
    SamePurchase,
    /// <summary>The files contain evidence of the same person.</summary>
    SamePerson,
    /// <summary>The files contain evidence of the same organization.</summary>
    SameOrganization,
    /// <summary>The files contain evidence of the same topic.</summary>
    SameTopic,
    /// <summary>The files contain evidence of the same event.</summary>
    SameEvent,
    /// <summary>The files appear to be versions of the same material.</summary>
    SameVersion,
    /// <summary>The files form one document set, including duplicate content.</summary>
    DocumentSet,
    /// <summary>The relationship was explicitly created by the user.</summary>
    Manual,
    /// <summary>The relationship uses a bounded user-defined category name.</summary>
    Custom,
}

/// <summary>Expresses deterministic confidence without presenting fabricated percentages.</summary>
public enum RelationshipConfidence
{
    /// <summary>One limited but concrete evidence item supports the relationship.</summary>
    Low,
    /// <summary>Multiple independent evidence items support the relationship.</summary>
    Medium,
    /// <summary>A strong identifier or several independent signals support the relationship.</summary>
    High,
    /// <summary>The user explicitly confirmed or created the relationship.</summary>
    Confirmed,
}

/// <summary>Identifies the source of one retained relationship evidence item.</summary>
public enum RelationshipEvidenceKind
{
    /// <summary>The files have identical content fingerprints.</summary>
    DuplicateContent,
    /// <summary>Filename terms or a document identifier matched.</summary>
    Filename,
    /// <summary>The files share a source-relative folder.</summary>
    Folder,
    /// <summary>Indexed metadata matched.</summary>
    Metadata,
    /// <summary>Indexed timestamps were close enough to corroborate other evidence.</summary>
    Timestamp,
    /// <summary>Bounded extracted-text fingerprints matched.</summary>
    ExtractedText,
    /// <summary>Bounded OCR-text fingerprints matched.</summary>
    OcrText,
    /// <summary>Bounded summaries matched.</summary>
    Summary,
    /// <summary>Generated keywords overlapped.</summary>
    Keyword,
    /// <summary>Accepted tags overlapped.</summary>
    Tag,
    /// <summary>Related-concept representations were similar.</summary>
    SemanticConcept,
    /// <summary>Workflow provenance matched.</summary>
    Workflow,
    /// <summary>Deterministic camera, device, duration, or capture metadata corroborated the relationship.</summary>
    MediaMetadata,
    /// <summary>Bounded local transcript evidence matched.</summary>
    MediaTranscript,
    /// <summary>Bounded image or representative-frame OCR evidence matched.</summary>
    MediaOcr,
    /// <summary>The files share a non-generic bounded topic.</summary>
    ContentTopic,
    /// <summary>The files share a bounded textual entity.</summary>
    ContentEntity,
    /// <summary>The user explicitly supplied the relationship.</summary>
    Manual,
}

/// <summary>Identifies a persistent user correction applied to one file pair.</summary>
public enum RelationshipDecision
{
    /// <summary>No user correction has been recorded.</summary>
    None,
    /// <summary>The user confirmed the current suggestion.</summary>
    Confirmed,
    /// <summary>The user dismissed the current suggestion.</summary>
    Rejected,
    /// <summary>The pair remains related even when automatic evidence changes.</summary>
    AlwaysRelate,
    /// <summary>The pair must not be related automatically.</summary>
    NeverRelate,
}

/// <summary>Identifies how a Smart Collection was created.</summary>
public enum SmartCollectionCreationSource
{
    /// <summary>The collection was produced from deterministic relationship evidence.</summary>
    Automatic,
    /// <summary>The collection was explicitly created or converted by the user.</summary>
    Manual,
    /// <summary>The collection is the result of an explicit merge.</summary>
    Merged,
}

/// <summary>Identifies how a member entered a Smart Collection.</summary>
public enum CollectionMembershipSource
{
    /// <summary>Evidence-backed automatic analysis added the member.</summary>
    Automatic,
    /// <summary>The user added or retained the member explicitly.</summary>
    Manual,
}

/// <summary>Contains one bounded evidence item used by the relationship engine.</summary>
public sealed record RelationshipEvidence(
    RelationshipEvidenceKind Kind,
    string EvidenceKey,
    string Explanation);

/// <summary>Contains one provider-neutral persisted relationship.</summary>
public sealed record FileRelationship
{
    /// <summary>Gets the stable relationship identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the first canonical file identifier.</summary>
    public required string FirstFileId { get; init; }

    /// <summary>Gets the second canonical file identifier.</summary>
    public required string SecondFileId { get; init; }

    /// <summary>Gets the relationship category.</summary>
    public required RelationshipType Type { get; init; }

    /// <summary>Gets a user-defined type name when <see cref="Type"/> is Custom.</summary>
    public string? CustomType { get; init; }

    /// <summary>Gets the deterministic confidence level.</summary>
    public required RelationshipConfidence Confidence { get; init; }

    /// <summary>Gets the exact bounded evidence used to create the relationship.</summary>
    public IReadOnlyList<RelationshipEvidence> Evidence { get; init; } = [];

    /// <summary>Gets the stable originating algorithm name.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Gets the originating algorithm version.</summary>
    public required string AlgorithmVersion { get; init; }

    /// <summary>Gets when the relationship was first retained.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets when the evidence was last validated.</summary>
    public required DateTimeOffset LastValidatedAtUtc { get; init; }

    /// <summary>Gets the current persistent user correction.</summary>
    public RelationshipDecision Decision { get; init; }

    /// <summary>Gets whether the relationship was created manually.</summary>
    public bool IsManual { get; init; }

    /// <summary>Gets a concise explanation composed only from retained evidence.</summary>
    public string Explanation => string.Join(
        "; ",
        Evidence.Select(item => item.Explanation).Distinct(StringComparer.Ordinal));
}

/// <summary>Contains bounded indexed fields used by deterministic relationship analysis.</summary>
public sealed record RelationshipFileDocument
{
    /// <summary>Gets the durable file identifier.</summary>
    public required string FileId { get; init; }

    /// <summary>Gets the durable source identifier.</summary>
    public required string SourceId { get; init; }

    /// <summary>Gets the display-safe source name.</summary>
    public required string SourceName { get; init; }

    /// <summary>Gets the local full path used for opening only.</summary>
    public required string FullPath { get; init; }

    /// <summary>Gets the source-relative path.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Gets the filename.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the source-relative containing folder.</summary>
    public required string FolderName { get; init; }

    /// <summary>Gets the normalized extension.</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>Gets the observed content hash when available.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Gets the observed creation time.</summary>
    public DateTimeOffset? CreationTimeUtc { get; init; }

    /// <summary>Gets the observed modification time.</summary>
    public DateTimeOffset? ModifiedTimeUtc { get; init; }

    /// <summary>Gets bounded indexed metadata.</summary>
    public string MetadataText { get; init; } = string.Empty;

    /// <summary>Gets bounded extracted document text.</summary>
    public string? ExtractedText { get; init; }

    /// <summary>Gets bounded OCR text.</summary>
    public string? OcrText { get; init; }

    /// <summary>Gets structured bounded media evidence when retained.</summary>
    public IndexedMediaEvidence? MediaEvidence { get; init; }

    /// <summary>Gets bounded content-intelligence evidence retained by the index.</summary>
    public IndexedContentIntelligence? ContentIntelligence { get; init; }

    /// <summary>Gets a bounded summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets bounded generated keywords.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Gets bounded accepted tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets one bounded related-concept representation.</summary>
    public IReadOnlyList<float>? SemanticRepresentation { get; init; }

    /// <summary>Gets whether all applicable indexing stages completed.</summary>
    public bool IsFullyIndexed { get; init; }

    /// <summary>Gets whether relationship analysis is excluded for this file.</summary>
    public bool RelationshipAnalysisSuppressed { get; init; }
}

/// <summary>Contains bounded, indexable features used to select relationship candidates.</summary>
public sealed record RelationshipFeatureSet(
    string FileId,
    string NormalizedStem,
    string FolderKey,
    string? ContentHash,
    long? DateBucket,
    string? ExtractedTextFingerprint,
    string? OcrTextFingerprint,
    string? SummaryFingerprint,
    IReadOnlyList<string> KeywordKeys,
    string FeatureVersion)
{
    /// <summary>Gets a fingerprint of bounded local transcript text.</summary>
    public string? MediaTranscriptFingerprint { get; init; }

    /// <summary>Gets a fingerprint of bounded image or representative-frame OCR.</summary>
    public string? MediaOcrFingerprint { get; init; }

    /// <summary>Gets a normalized camera or device key used only for bounded candidate selection.</summary>
    public string? MediaDeviceKey { get; init; }

    /// <summary>Gets the embedded capture-date bucket when a reliable offset was retained.</summary>
    public long? CaptureDateBucket { get; init; }
}

/// <summary>Suggests an evidence-backed automatic collection for one relationship.</summary>
public sealed record SmartCollectionSuggestion(
    string ContextKey,
    string Title,
    string Description,
    string RelationshipSummary,
    RelationshipType ContextType,
    RelationshipConfidence Confidence,
    string FirstFileId,
    string SecondFileId,
    string RelationshipId);

/// <summary>Contains one relationship and its optional automatic collection suggestion.</summary>
public sealed record RelationshipProposal(
    FileRelationship Relationship,
    SmartCollectionSuggestion? Collection);

/// <summary>Contains one atomic incremental relationship-analysis result.</summary>
public sealed record RelationshipAnalysisBatch(
    string FileId,
    RelationshipFeatureSet Features,
    int CandidateCount,
    IReadOnlyList<RelationshipProposal> Proposals,
    string Algorithm,
    string AlgorithmVersion,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration);

/// <summary>Reports bounded incremental relationship-analysis work.</summary>
public sealed record RelationshipAnalysisResult(
    string FileId,
    int CandidateCount,
    int RelationshipCount,
    int CollectionSuggestionCount,
    TimeSpan Duration,
    bool Skipped,
    string Message);

/// <summary>Contains one persisted Smart Collection.</summary>
public sealed record SmartCollection
{
    /// <summary>Gets the stable collection identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the display title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the bounded collection description.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the evidence-derived relationship summary.</summary>
    public required string RelationshipSummary { get; init; }

    /// <summary>Gets the dominant context category.</summary>
    public required RelationshipType ContextType { get; init; }

    /// <summary>Gets the deterministic confidence level.</summary>
    public required RelationshipConfidence Confidence { get; init; }

    /// <summary>Gets how the collection was created.</summary>
    public required SmartCollectionCreationSource CreationSource { get; init; }

    /// <summary>Gets the number of active indexed members.</summary>
    public int MemberCount { get; init; }

    /// <summary>Gets whether the user pinned the collection.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Gets whether the user supplied the current title.</summary>
    public bool IsUserRenamed { get; init; }

    /// <summary>Gets when collection membership or metadata last changed.</summary>
    public required DateTimeOffset LastUpdatedAtUtc { get; init; }
}

/// <summary>Contains one file membership within a Smart Collection.</summary>
public sealed record SmartCollectionMember(
    string CollectionId,
    string FileId,
    string FileName,
    string FullPath,
    string SourceName,
    CollectionMembershipSource MembershipSource,
    DateTimeOffset AddedAtUtc);

/// <summary>Contains one optional evidence-based collection timeline event.</summary>
public sealed record CollectionTimelineEvent(
    string FileId,
    string FileName,
    DateTimeOffset OccurredAtUtc,
    string Label,
    string TimestampSource);

/// <summary>Contains one fully inspectable collection projection.</summary>
public sealed record SmartCollectionDetails(
    SmartCollection Collection,
    IReadOnlyList<SmartCollectionMember> Members,
    IReadOnlyList<FileRelationship> Relationships,
    IReadOnlyList<CollectionTimelineEvent> Timeline);

/// <summary>Contains one related-file row with actual evidence and provenance.</summary>
public sealed record RelatedFile
{
    /// <summary>Gets the related file identifier.</summary>
    public required string FileId { get; init; }

    /// <summary>Gets the related filename.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the related full path for explicit open actions.</summary>
    public required string FullPath { get; init; }

    /// <summary>Gets the display-safe source name.</summary>
    public required string SourceName { get; init; }

    /// <summary>Gets the persisted relationship.</summary>
    public required FileRelationship Relationship { get; init; }
}

/// <summary>Identifies a deterministic Related Files sort order.</summary>
public enum RelatedFileSort
{
    /// <summary>Orders by confidence, type, then filename.</summary>
    Confidence,
    /// <summary>Orders by relationship category, confidence, then filename.</summary>
    Relationship,
    /// <summary>Orders by filename.</summary>
    FileName,
    /// <summary>Orders by most recently validated relationship.</summary>
    LastValidated,
}

/// <summary>Describes one relationship-aware Search expansion.</summary>
public sealed record RelationshipSearchExpansion(
    string SeedFileId,
    string RelatedFileId,
    RelationshipType Type,
    RelationshipConfidence Confidence,
    string Explanation,
    string? CollectionTitle);

/// <summary>Contains a privacy-safe aggregate relationship diagnostic snapshot.</summary>
public sealed record RelationshipDiagnosticsSnapshot(
    long RelationshipCount,
    long CollectionCount,
    long EvidenceCount,
    long ManualOverrideCount,
    long RejectedCount,
    long ExcludedFileCount,
    DateTimeOffset? LastAnalysisAtUtc,
    TimeSpan? LastAnalysisDuration,
    int LastCandidateCount,
    int LastGeneratedRelationshipCount,
    int LastGeneratedCollectionCount,
    string AlgorithmVersion,
    int RepairOperationCount);

/// <summary>Contains a controlled index-only relationship operation result.</summary>
public sealed record RelationshipOperationResult(
    bool Applied,
    int AffectedRelationshipCount,
    int AffectedCollectionCount,
    string Message);

/// <summary>Provides a compact relationship signal consumed by Search ranking.</summary>
public sealed record SearchRelationshipContext(
    string SeedFileId,
    RelationshipType Type,
    RelationshipConfidence Confidence,
    string Explanation,
    string? CollectionTitle)
{
    /// <summary>Maps the context to an actual Search ranking component.</summary>
    public SearchRankingComponent ToRankingComponent() => new(
        SearchRankingSignalKind.RelationshipContext,
        "relationship context",
        Confidence switch
        {
            RelationshipConfidence.Confirmed => 4,
            RelationshipConfidence.High => 3,
            RelationshipConfidence.Medium => 2,
            _ => 1,
        },
        Explanation);
}
