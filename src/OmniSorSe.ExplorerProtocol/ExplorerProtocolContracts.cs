using System.Text.Json;

namespace OmniSorSe.ExplorerProtocol;

/// <summary>Defines the independently versioned OmniExplorer protocol.</summary>
public static class ExplorerProtocolVersion
{
    /// <summary>Gets the breaking protocol version.</summary>
    public const int Major = 1;

    /// <summary>Gets the additive protocol version.</summary>
    public const int Minor = 0;

    /// <summary>Gets the display form of the protocol version.</summary>
    public const string Display = "1.0";
}

/// <summary>Identifies one read-only Explorer operation.</summary>
public enum ExplorerOperation
{
    /// <summary>Returns version, capability, and limit information.</summary>
    GetProtocolInfo,
    /// <summary>Returns only roots authorized for the current session.</summary>
    GetAccessibleRoots,
    /// <summary>Returns one bounded page of structural children.</summary>
    GetChildren,
    /// <summary>Returns one bounded structural/context neighborhood.</summary>
    GetNeighborhood,
    /// <summary>Runs unified deterministic-first Search.</summary>
    Search,
    /// <summary>Returns bounded existing Related Files evidence.</summary>
    GetRelated,
    /// <summary>Returns bounded details for one authorized node.</summary>
    GetNodeDetails,
}

/// <summary>Identifies a capability supported by the protocol surface.</summary>
[Flags]
public enum ExplorerCapability
{
    /// <summary>No optional capability is declared.</summary>
    None = 0,
    /// <summary>Authorized structural roots and containment are available.</summary>
    Structure = 1 << 0,
    /// <summary>Unified local Search is available.</summary>
    Search = 1 << 1,
    /// <summary>Bounded contextual neighborhoods are available.</summary>
    Context = 1 << 2,
    /// <summary>Existing Related Files evidence is available.</summary>
    RelatedFiles = 1 << 3,
    /// <summary>Retained media metadata can be projected.</summary>
    MediaIntelligence = 1 << 4,
    /// <summary>Retained Content Intelligence can be projected.</summary>
    ContentIntelligence = 1 << 5,
    /// <summary>Retained OCR evidence may explain Search results.</summary>
    Ocr = 1 << 6,
    /// <summary>Retained local transcript evidence may explain Search results.</summary>
    Transcripts = 1 << 7,
    /// <summary>Retained bounded topics can be projected.</summary>
    Topics = 1 << 8,
    /// <summary>Retained bounded textual entities can be projected.</summary>
    Entities = 1 << 9,
    /// <summary>Retained bounded summaries can be projected.</summary>
    Summaries = 1 << 10,
}

/// <summary>Identifies a visualization-neutral node category.</summary>
public enum ExplorerNodeKind
{
    /// <summary>An explicitly indexed/authorized source root.</summary>
    Source,
    /// <summary>A virtual structural folder represented by indexed descendants.</summary>
    Folder,
    /// <summary>An indexed file.</summary>
    File,
}

/// <summary>Identifies the small stable Explorer edge taxonomy.</summary>
public enum ExplorerEdgeKind
{
    /// <summary>Structural parent/child containment.</summary>
    Contains,
    /// <summary>An existing Related Files relationship.</summary>
    Related,
    /// <summary>A shared bounded topic.</summary>
    Topic,
    /// <summary>A shared bounded textual entity.</summary>
    Entity,
    /// <summary>A bounded temporal relationship.</summary>
    Temporal,
    /// <summary>Retained OCR evidence contributed.</summary>
    Ocr,
    /// <summary>Retained transcript evidence contributed.</summary>
    Transcript,
}

/// <summary>Classifies how one edge was established.</summary>
public enum ExplorerEvidenceClass
{
    /// <summary>The edge describes source/folder containment.</summary>
    Structural,
    /// <summary>The edge came from deterministic indexed evidence.</summary>
    Deterministic,
    /// <summary>The edge uses optional derived evidence and is not objective truth.</summary>
    Derived,
}

/// <summary>Defines stable, privacy-safe failure categories.</summary>
public enum ExplorerErrorCode
{
    /// <summary>The request did not include valid session authorization.</summary>
    Unauthorized,
    /// <summary>The session has reached its absolute expiry.</summary>
    SessionExpired,
    /// <summary>The requested protocol major version is incompatible.</summary>
    UnsupportedProtocol,
    /// <summary>The requested capability is unavailable.</summary>
    CapabilityUnavailable,
    /// <summary>A previously issued node no longer exists.</summary>
    NodeNotFound,
    /// <summary>The request is not within the session's authorized indexed scope.</summary>
    OutOfScope,
    /// <summary>The framed request or a string field exceeded its byte/character bound.</summary>
    RequestTooLarge,
    /// <summary>A requested count, depth, or continuation exceeded server limits.</summary>
    LimitExceeded,
    /// <summary>The request payload was malformed.</summary>
    MalformedRequest,
    /// <summary>The operation was cancelled cooperatively.</summary>
    Cancelled,
    /// <summary>The bounded service is busy or shutting down.</summary>
    TemporarilyUnavailable,
    /// <summary>An unexpected failure was isolated without exposing internals.</summary>
    InternalFailure,
}

/// <summary>Defines the server-enforced limits advertised during negotiation.</summary>
public sealed record ExplorerProtocolLimits(
    int MaximumRequestBytes,
    int MaximumResponseBytes,
    int MaximumQueryCharacters,
    int MaximumNodes,
    int MaximumEdges,
    int MaximumSearchResults,
    int MaximumRelatedResults,
    int MaximumDepth,
    int MaximumSnippetCharacters,
    int MaximumTopics,
    int MaximumEntities,
    int MaximumReasonCharacters,
    int MaximumConcurrentRequests,
    int RequestTimeoutSeconds);

/// <summary>Contains protocol/application identity, capabilities, and hard bounds.</summary>
public sealed record ExplorerProtocolInfo(
    int ProtocolMajor,
    int ProtocolMinor,
    string ApplicationName,
    string ApplicationVersion,
    ExplorerCapability Capabilities,
    ExplorerProtocolLimits Limits,
    bool IsReadOnly,
    string Transport);

/// <summary>Contains one small visualization-neutral node.</summary>
public sealed record ExplorerNode(
    string Id,
    string Name,
    ExplorerNodeKind Kind,
    string? ParentId,
    string? Extension,
    long? SizeBytes,
    string? AuthorizedPath,
    IReadOnlyDictionary<string, string> Metadata,
    int ChildCount,
    int RelationshipCount);

/// <summary>Contains one bounded, explainable edge.</summary>
public sealed record ExplorerEdge(
    string SourceId,
    string TargetId,
    ExplorerEdgeKind Kind,
    int Strength,
    string Reason,
    ExplorerEvidenceClass EvidenceClass,
    string Provenance);

/// <summary>Contains one bounded page of nodes.</summary>
public sealed record ExplorerNodePage(
    IReadOnlyList<ExplorerNode> Nodes,
    int TotalAvailable,
    bool IsTruncated,
    string? ContinuationToken);

/// <summary>Contains one bounded neighborhood suitable for future aggregation.</summary>
public sealed record ExplorerNeighborhood(
    string FocusNodeId,
    IReadOnlyList<ExplorerNode> Nodes,
    IReadOnlyList<ExplorerEdge> Edges,
    bool IsTruncated,
    string? ContinuationToken);

/// <summary>Contains one bounded retained topic or textual entity.</summary>
public sealed record ExplorerConcept(
    string Name,
    string Kind,
    string Confidence,
    bool IsAiDerived,
    string Provider);

/// <summary>Contains safe bounded media details, excluding full OCR/transcript and precise GPS.</summary>
public sealed record ExplorerMediaDetails(
    string Kind,
    string? Container,
    int? Width,
    int? Height,
    double? DurationSeconds,
    string? Device,
    DateTimeOffset? CapturedAtUtc,
    string? VideoCodec,
    string? AudioCodec,
    bool HasOcrEvidence,
    bool HasTranscriptEvidence);

/// <summary>Contains bounded details for one authorized node.</summary>
public sealed record ExplorerNodeDetails(
    ExplorerNode Node,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc,
    string? Summary,
    IReadOnlyList<ExplorerConcept> Topics,
    IReadOnlyList<ExplorerConcept> Entities,
    ExplorerMediaDetails? Media,
    IReadOnlyList<string> RelationshipSummaries,
    bool IsFullyIndexed);

/// <summary>Requests one bounded page of structural children.</summary>
public sealed record ExplorerChildrenRequest(
    string ParentNodeId,
    int? MaximumResults = null,
    string? ContinuationToken = null);

/// <summary>Requests one bounded structural and optional contextual neighborhood.</summary>
public sealed record ExplorerNeighborhoodRequest(
    string NodeId,
    int? Depth = null,
    int? MaximumNodes = null,
    int? MaximumEdges = null,
    bool IncludeContext = true);

/// <summary>Requests one bounded deterministic-first unified Search.</summary>
public sealed record ExplorerSearchRequest(
    string Query,
    int? MaximumResults = null,
    bool IncludeContext = true);

/// <summary>Contains one grounded Search result.</summary>
public sealed record ExplorerSearchHit(
    ExplorerNode Node,
    int Rank,
    double Score,
    string Explanation,
    string? Snippet,
    string? EvidenceSource);

/// <summary>Contains one bounded Search response.</summary>
public sealed record ExplorerSearchResult(
    IReadOnlyList<ExplorerSearchHit> Results,
    bool IsTruncated,
    string Coverage,
    bool UsedAiAssistance);

/// <summary>Requests bounded existing Related Files evidence.</summary>
public sealed record ExplorerRelatedRequest(string NodeId, int? MaximumResults = null);

/// <summary>Requests bounded details for one authorized node.</summary>
public sealed record ExplorerNodeDetailsRequest(string NodeId);

/// <summary>Contains one authorized Related Files response.</summary>
public sealed record ExplorerRelatedResult(
    IReadOnlyList<ExplorerNode> Nodes,
    IReadOnlyList<ExplorerEdge> Edges,
    bool IsTruncated);

/// <summary>Contains the fixed request envelope accepted by protocol v1.</summary>
public sealed record ExplorerRequestEnvelope(
    int ProtocolMajor,
    string RequestId,
    string SessionId,
    string AuthorizationToken,
    ExplorerOperation Operation,
    JsonElement Payload);

/// <summary>Contains a stable error without exception, stack, path, or database detail.</summary>
public sealed record ExplorerProtocolError(ExplorerErrorCode Code, string Message, bool Retryable);

/// <summary>Contains the fixed response envelope produced by protocol v1.</summary>
public sealed record ExplorerResponseEnvelope(
    int ProtocolMajor,
    string RequestId,
    bool Success,
    JsonElement? Payload,
    ExplorerProtocolError? Error);
