namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Describes exact inputs used to build or reuse one stable graph identity.</summary>
public sealed record GraphIdentityInput
{
    /// <summary>Gets the requested node kind.</summary>
    public required GraphNodeKind Kind { get; init; }
    /// <summary>Gets the provider-defined identity scope.</summary>
    public required string Scope { get; init; }
    /// <summary>Gets the exact provider-authoritative canonical key.</summary>
    public required string CanonicalKey { get; init; }
    /// <summary>Gets the version of normalization applied to the canonical key.</summary>
    public required string NormalizationVersion { get; init; }
    /// <summary>Gets an existing provider-neutral stable ID for File, Source, Collection, or Manual Entity nodes.</summary>
    public string? ExistingStableId { get; init; }
    /// <summary>Gets the path comparison rule when the identity contains a relative path.</summary>
    public GraphPathComparison PathComparison { get; init; } = GraphPathComparison.CaseSensitive;
    /// <summary>Gets the content-hash algorithm version for Document Set identity.</summary>
    public string? HashAlgorithmVersion { get; init; }
}

/// <summary>Identifies the conservative outcome of identity resolution.</summary>
public enum GraphIdentityResolutionStatus
{
    /// <summary>A stable mechanical or manual identity was resolved.</summary>
    Resolved,
    /// <summary>The input can only become an inactive confirmation-required suggestion.</summary>
    SuggestionRequired,
    /// <summary>The input is not part of the supported stable graph.</summary>
    Rejected,
    /// <summary>Conflicting canonical inputs require selective repair.</summary>
    RepairRequired,
}

/// <summary>Contains one conservative identity resolution with retained collision inputs.</summary>
public sealed record GraphIdentityResolution(
    GraphIdentityResolutionStatus Status,
    string? NodeId,
    string CanonicalInputs,
    string Algorithm,
    string AlgorithmVersion,
    string ReasonCode);

/// <summary>Identifies one provider-neutral source observation type.</summary>
public enum GraphProjectionObservationKind
{
    /// <summary>An indexed source was observed.</summary>
    Source,
    /// <summary>An indexed file was observed.</summary>
    File,
    /// <summary>An authoritative v1.9 relationship was observed.</summary>
    Relationship,
    /// <summary>An authoritative v1.9 Smart Collection was observed.</summary>
    Collection,
    /// <summary>An authoritative v1.9 collection membership was observed.</summary>
    CollectionMembership,
    /// <summary>An authoritative v1.9 decision or tombstone was observed.</summary>
    LegacyDecision,
    /// <summary>A formerly observed stable key was deleted after a complete manifest.</summary>
    Deletion,
}

/// <summary>Provides immutable manifest-scoped facts shared by projection observations.</summary>
public abstract record GraphProjectionObservation
{
    /// <summary>Gets the observation category.</summary>
    public abstract GraphProjectionObservationKind Kind { get; }
    /// <summary>Gets the stable provider key used for ordered paging.</summary>
    public required string StableKey { get; init; }
    /// <summary>Gets a canonical provider hash covering all graph-relevant fields.</summary>
    public required string CanonicalRowHash { get; init; }
    /// <summary>Gets the provider observation revision.</summary>
    public required long Revision { get; init; }
    /// <summary>Gets when the provider observed the source fact.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }
    /// <summary>Gets whether current privacy authority excludes this observation.</summary>
    public bool IsExcluded { get; init; }
}

/// <summary>Contains one indexed source observation without exposing its absolute path.</summary>
public sealed record GraphSourceObservation : GraphProjectionObservation
{
    /// <inheritdoc />
    public override GraphProjectionObservationKind Kind => GraphProjectionObservationKind.Source;
    /// <summary>Gets the existing stable source ID.</summary>
    public required string SourceId { get; init; }
    /// <summary>Gets the bounded display-safe source label.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Gets the provider path-semantics version.</summary>
    public required string PathSemanticsVersion { get; init; }
    /// <summary>Gets the provider path comparison behavior.</summary>
    public required GraphPathComparison PathComparison { get; init; }
}

/// <summary>Contains bounded file facts already retained by the v1.9 index.</summary>
public sealed record GraphFileObservation : GraphProjectionObservation
{
    /// <inheritdoc />
    public override GraphProjectionObservationKind Kind => GraphProjectionObservationKind.File;
    /// <summary>Gets the existing source-scoped file ID.</summary>
    public required string FileId { get; init; }
    /// <summary>Gets the existing source ID.</summary>
    public required string SourceId { get; init; }
    /// <summary>Gets the bounded filename.</summary>
    public required string FileName { get; init; }
    /// <summary>Gets the source-relative path, never an absolute path.</summary>
    public required string RelativePath { get; init; }
    /// <summary>Gets the source-relative containing folder.</summary>
    public required string FolderRelativePath { get; init; }
    /// <summary>Gets the provider path-semantics version.</summary>
    public required string PathSemanticsVersion { get; init; }
    /// <summary>Gets the provider path comparison behavior.</summary>
    public required GraphPathComparison PathComparison { get; init; }
    /// <summary>Gets the non-negative observed file length.</summary>
    public required long Length { get; init; }
    /// <summary>Gets the indexed creation timestamp when the provider has one.</summary>
    public DateTimeOffset? CreationTimeUtc { get; init; }
    /// <summary>Gets the indexed modification timestamp when the provider has one.</summary>
    public DateTimeOffset? ModifiedTimeUtc { get; init; }
    /// <summary>Gets whether bounded basic metadata is present in the authoritative index.</summary>
    public bool HasBasicMetadata { get; init; }
    /// <summary>Gets an exact validated content hash when available.</summary>
    public string? ContentHash { get; init; }
    /// <summary>Gets the content-hash algorithm version when a hash is present.</summary>
    public string? ContentHashAlgorithmVersion { get; init; }
    /// <summary>Gets whether relationship and graph context is suppressed for this file.</summary>
    public bool RelationshipAnalysisSuppressed { get; init; }
}

/// <summary>Contains one retained evidence item from an authoritative projection source.</summary>
public sealed record GraphProjectionEvidence(
    string StableKey,
    GraphEvidenceKind Kind,
    string EvidenceKey,
    string ExplanationTemplateCode,
    string Explanation,
    string CanonicalObservationHash);

/// <summary>Contains one authoritative v1.9 relationship observation.</summary>
public sealed record GraphRelationshipObservation : GraphProjectionObservation
{
    /// <inheritdoc />
    public override GraphProjectionObservationKind Kind => GraphProjectionObservationKind.Relationship;
    /// <summary>Gets the v1.9 relationship ID.</summary>
    public required string RelationshipId { get; init; }
    /// <summary>Gets the first existing file ID.</summary>
    public required string FirstFileId { get; init; }
    /// <summary>Gets the second existing file ID.</summary>
    public required string SecondFileId { get; init; }
    /// <summary>Gets the bounded relationship type code.</summary>
    public required string RelationshipType { get; init; }
    /// <summary>Gets deterministic legacy confidence mapped without probability.</summary>
    public required GraphConfidenceLevel Confidence { get; init; }
    /// <summary>Gets the exact retained relationship evidence.</summary>
    public IReadOnlyList<GraphProjectionEvidence> Evidence { get; init; } = [];
    /// <summary>Gets the originating v1.9 algorithm.</summary>
    public required string Algorithm { get; init; }
    /// <summary>Gets the originating v1.9 algorithm version.</summary>
    public required string AlgorithmVersion { get; init; }
    /// <summary>Gets whether the legacy relationship was explicitly created by the user.</summary>
    public bool IsManual { get; init; }
    /// <summary>Gets whether current v1.9 authority rejects or suppresses the relationship.</summary>
    public bool IsRejected { get; init; }
}

/// <summary>Contains one authoritative v1.9 Smart Collection observation.</summary>
public sealed record GraphCollectionObservation : GraphProjectionObservation
{
    /// <inheritdoc />
    public override GraphProjectionObservationKind Kind => GraphProjectionObservationKind.Collection;
    /// <summary>Gets the existing stable collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the bounded current authoritative title.</summary>
    public required string Title { get; init; }
    /// <summary>Gets whether the collection is manual or contains user-controlled metadata.</summary>
    public bool IsManual { get; init; }
    /// <summary>Gets whether an authoritative tombstone suppresses the collection.</summary>
    public bool IsForgotten { get; init; }
}

/// <summary>Contains one authoritative v1.9 Smart Collection membership.</summary>
public sealed record GraphCollectionMembershipObservation : GraphProjectionObservation
{
    /// <inheritdoc />
    public override GraphProjectionObservationKind Kind => GraphProjectionObservationKind.CollectionMembership;
    /// <summary>Gets the existing collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the existing member file ID.</summary>
    public required string FileId { get; init; }
    /// <summary>Gets whether the membership is user-controlled.</summary>
    public bool IsManual { get; init; }
}

/// <summary>Contains a non-authoritative mirror input for a v1.9 decision or tombstone.</summary>
public sealed record GraphLegacyDecisionObservation : GraphProjectionObservation
{
    /// <inheritdoc />
    public override GraphProjectionObservationKind Kind => GraphProjectionObservationKind.LegacyDecision;
    /// <summary>Gets the authoritative v1.9 decision namespace.</summary>
    public required string DecisionNamespace { get; init; }
    /// <summary>Gets the stable key within that namespace.</summary>
    public required string LegacyDecisionKey { get; init; }
    /// <summary>Gets the bounded action code.</summary>
    public required string ActionCode { get; init; }
    /// <summary>Gets whether the legacy source retired this decision.</summary>
    public bool IsRetired { get; init; }
}

/// <summary>Contains a deletion proven by a completed authoritative manifest.</summary>
public sealed record GraphDeletionObservation : GraphProjectionObservation
{
    /// <inheritdoc />
    public override GraphProjectionObservationKind Kind => GraphProjectionObservationKind.Deletion;
    /// <summary>Gets the former observation category.</summary>
    public required GraphProjectionObservationKind DeletedKind { get; init; }
    /// <summary>Gets the former provider-stable key.</summary>
    public required string DeletedStableKey { get; init; }
}

/// <summary>Identifies one immutable completed source snapshot.</summary>
public sealed record GraphProjectionSnapshot(
    string ManifestId,
    long Revision,
    string LegacyDecisionManifestId,
    long PrivacySequence,
    DateTimeOffset CompletedAtUtc,
    string CanonicalManifestHash,
    long TotalObservationCount,
    IReadOnlyList<GraphObservationKindCount> ObservationCounts)
{
    /// <summary>Gets the graph-native decision sequence captured by the coordinator for this run.</summary>
    public long GraphDecisionSequence { get; init; }
    /// <summary>Gets the validated graph-native decision checkpoint captured for this run.</summary>
    public string GraphDecisionCheckpointId { get; init; } = string.Empty;
}

/// <summary>Contains a terminal manifest count for one observation category.</summary>
public sealed record GraphObservationKindCount(GraphProjectionObservationKind Kind, long Count);

/// <summary>Provides an opaque provider-owned continuation position.</summary>
public sealed record GraphProjectionCursor(string Value);

/// <summary>Contains one stable-key-ordered source page.</summary>
public sealed record GraphProjectionPage(
    string ManifestId,
    long SnapshotRevision,
    long PageSequence,
    int ObservationCount,
    string CanonicalPageHash,
    IReadOnlyList<GraphProjectionObservation> Observations,
    GraphProjectionCursor? NextCursor,
    bool IsLastPage);

/// <summary>Contains retained collision inputs for one graph node.</summary>
public sealed record GraphIdentity(
    string NodeId,
    GraphNodeKind Kind,
    string Scope,
    string CanonicalKey,
    string NormalizationVersion,
    string CanonicalInputs);

/// <summary>Contains one privacy-bounded provider-neutral graph node.</summary>
public sealed record GraphNode
{
    /// <summary>Gets the resolved stable identity.</summary>
    public required GraphIdentity Identity { get; init; }
    /// <summary>Gets the bounded display label.</summary>
    public required string DisplayLabel { get; init; }
    /// <summary>
    /// Gets the authoritative indexed-source stable ID that owns this node when one is known.
    /// This remains provider-neutral and must not be inferred from a hashed graph node ID.
    /// </summary>
    public string? OwningSourceId { get; init; }
    /// <summary>Gets the record origin.</summary>
    public required GraphOrigin Origin { get; init; }
    /// <summary>Gets the completed source manifest that authorized this node.</summary>
    public required string SourceManifestId { get; init; }
    /// <summary>Gets the canonical source observation hash.</summary>
    public required string ObservationHash { get; init; }
    /// <summary>Gets the algorithm name.</summary>
    public required string Algorithm { get; init; }
    /// <summary>Gets the algorithm version.</summary>
    public required string AlgorithmVersion { get; init; }
    /// <summary>Gets when the record was first created.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>Gets when its authority was last validated.</summary>
    public required DateTimeOffset LastValidatedAtUtc { get; init; }
    /// <summary>Gets current component freshness.</summary>
    public GraphFreshnessState Freshness { get; init; } = GraphFreshnessState.Current;
    /// <summary>Gets current component integrity.</summary>
    public GraphIntegrityState Integrity { get; init; } = GraphIntegrityState.Valid;
    /// <summary>Gets whether current authority permits ordinary display.</summary>
    public bool IsVisible { get; init; } = true;
}

/// <summary>Contains one resolvable retained graph evidence record.</summary>
public sealed record GraphEvidenceReference
{
    /// <summary>Gets the deterministic evidence ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the evidence kind.</summary>
    public required GraphEvidenceKind Kind { get; init; }
    /// <summary>Gets the source-stable evidence key.</summary>
    public required string SourceEvidenceKey { get; init; }
    /// <summary>Gets the explanation template code.</summary>
    public required string ExplanationTemplateCode { get; init; }
    /// <summary>Gets the bounded explanation rendered from retained facts.</summary>
    public required string Explanation { get; init; }
    /// <summary>Gets the completed source manifest ID.</summary>
    public required string SourceManifestId { get; init; }
    /// <summary>Gets the canonical observation hash.</summary>
    public required string ObservationHash { get; init; }
}

/// <summary>Contains one explicit bounded alias controlled by graph-native decisions.</summary>
public sealed record GraphAlias(
    string Id,
    string NodeId,
    string Label,
    string NormalizedLabel,
    GraphOrigin Origin,
    string? DecisionId,
    DateTimeOffset CreatedAtUtc);

/// <summary>Contains one bounded source mention that remains separate from confirmed identity.</summary>
public sealed record GraphMention
{
    /// <summary>Gets the deterministic mention ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the possible entity category.</summary>
    public required GraphSuggestionKind Kind { get; init; }
    /// <summary>Gets the observation that contained the mention.</summary>
    public required string SourceStableKey { get; init; }
    /// <summary>Gets the declared resolution scope.</summary>
    public required string Scope { get; init; }
    /// <summary>Gets the bounded display label.</summary>
    public required string Label { get; init; }
    /// <summary>Gets the exact normalized candidate key.</summary>
    public required string NormalizedKey { get; init; }
    /// <summary>Gets the extractor version.</summary>
    public required string ExtractorVersion { get; init; }
    /// <summary>Gets retained evidence IDs that actually support the mention.</summary>
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    /// <summary>Gets whether a graph-native user decision confirmed the candidate.</summary>
    public bool IsConfirmed { get; init; }
}

/// <summary>Identifies one bounded mechanical graph fact category.</summary>
public readonly record struct GraphFactKind
{
    /// <summary>Initializes a syntactically valid fact code.</summary>
    /// <param name="value">Lowercase provider-neutral code.</param>
    public GraphFactKind(string value) => Value = GraphCode.Validate(value, nameof(value));
    /// <summary>Gets the provider-neutral code.</summary>
    public string Value { get; }
    /// <summary>Gets the non-negative file-size fact.</summary>
    public static GraphFactKind FileSize { get; } = new("file-size");
    /// <summary>Gets the indexed creation timestamp fact.</summary>
    public static GraphFactKind CreatedTimestamp { get; } = new("created-timestamp");
    /// <summary>Gets the indexed modification timestamp fact.</summary>
    public static GraphFactKind ModifiedTimestamp { get; } = new("modified-timestamp");
    /// <summary>Gets whether the stable projection understands this fact kind.</summary>
    public bool IsStable => this == FileSize || this == CreatedTimestamp || this == ModifiedTimestamp;
    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Contains one bounded evidence-backed mechanical fact.</summary>
public sealed record GraphFact(
    string Id,
    string SubjectNodeId,
    GraphFactKind Kind,
    string CanonicalValue,
    IReadOnlyList<string> EvidenceIds,
    string AlgorithmVersion);

/// <summary>Contains one non-authoritative mirror of an existing v1.9 decision or tombstone.</summary>
public sealed record GraphLegacyDecisionMirror(
    string DecisionNamespace,
    string LegacyDecisionKey,
    string ActionCode,
    string LegacyDecisionManifestId,
    string CanonicalRowHash,
    bool IsRetired);

/// <summary>Contains one directed, evidence-backed graph edge.</summary>
public sealed record GraphEdge
{
    /// <summary>Gets the deterministic edge ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the edge kind.</summary>
    public required GraphEdgeKind Kind { get; init; }
    /// <summary>Gets the source node ID.</summary>
    public required string SourceNodeId { get; init; }
    /// <summary>Gets the target node ID.</summary>
    public required string TargetNodeId { get; init; }
    /// <summary>Gets the deterministic confidence level.</summary>
    public required GraphConfidenceLevel Confidence { get; init; }
    /// <summary>Gets the record origin.</summary>
    public required GraphOrigin Origin { get; init; }
    /// <summary>Gets the evidence IDs actually used to create this edge.</summary>
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    /// <summary>Gets the originating algorithm.</summary>
    public required string Algorithm { get; init; }
    /// <summary>Gets the originating algorithm version.</summary>
    public required string AlgorithmVersion { get; init; }
    /// <summary>Gets a fingerprint of all rule inputs.</summary>
    public required string InputFingerprint { get; init; }
    /// <summary>Gets when the edge was first created.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>Gets when retained evidence was last validated.</summary>
    public required DateTimeOffset LastValidatedAtUtc { get; init; }
    /// <summary>Gets current component freshness.</summary>
    public GraphFreshnessState Freshness { get; init; } = GraphFreshnessState.Current;
    /// <summary>Gets current component integrity.</summary>
    public GraphIntegrityState Integrity { get; init; } = GraphIntegrityState.Valid;
    /// <summary>Gets whether this is an explicit manual edge.</summary>
    public bool IsManual { get; init; }
}

/// <summary>Contains the deterministic replacement for one bounded graph component.</summary>
public sealed record GraphComponentProjection
{
    /// <summary>Gets the stable component key.</summary>
    public required string ComponentKey { get; init; }
    /// <summary>Gets the source observation revision.</summary>
    public required long ObservationRevision { get; init; }
    /// <summary>Gets a fingerprint of graph-relevant input and configuration.</summary>
    public required string InputFingerprint { get; init; }
    /// <summary>Gets the completed source manifest ID.</summary>
    public required string SourceManifestId { get; init; }
    /// <summary>Gets canonically ordered replacement nodes.</summary>
    public IReadOnlyList<GraphNode> Nodes { get; init; } = [];
    /// <summary>Gets canonically ordered replacement edges.</summary>
    public IReadOnlyList<GraphEdge> Edges { get; init; } = [];
    /// <summary>Gets canonically ordered replacement evidence.</summary>
    public IReadOnlyList<GraphEvidenceReference> Evidence { get; init; } = [];
    /// <summary>Gets endpoint node IDs that must already exist or be staged before publication.</summary>
    public IReadOnlyList<string> RequiredNodeIds { get; init; } = [];
    /// <summary>Gets canonically ordered graph-native aliases.</summary>
    public IReadOnlyList<GraphAlias> Aliases { get; init; } = [];
    /// <summary>Gets canonically ordered confirmation-gated mentions.</summary>
    public IReadOnlyList<GraphMention> Mentions { get; init; } = [];
    /// <summary>Gets canonically ordered bounded mechanical facts.</summary>
    public IReadOnlyList<GraphFact> Facts { get; init; } = [];
    /// <summary>Gets non-authoritative v1.9 decision mirror replacements.</summary>
    public IReadOnlyList<GraphLegacyDecisionMirror> LegacyDecisions { get; init; } = [];
    /// <summary>Gets whether this component represents a proven source deletion.</summary>
    public bool IsDeletion { get; init; }
}
