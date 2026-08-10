using System.Globalization;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Defines hard safety ceilings shared by graph providers and application services.</summary>
public static class GraphLimits
{
    /// <summary>Gets the default ordinary query page size.</summary>
    public const int DefaultPageSize = 50;
    /// <summary>Gets the largest ordinary query page.</summary>
    public const int MaximumPageSize = 100;
    /// <summary>Gets the largest projection-source page.</summary>
    public const int MaximumProjectionPageSize = 256;
    /// <summary>Gets the maximum evidence references retained by one edge.</summary>
    public const int MaximumEvidencePerEdge = 8;
    /// <summary>Gets the default stable incident-edge limit for one node.</summary>
    public const int DefaultEdgesPerNode = 64;
    /// <summary>Gets the hard stable incident-edge ceiling for one node.</summary>
    public const int MaximumEdgesPerNode = 128;
    /// <summary>Gets the default aliases retained for one entity.</summary>
    public const int DefaultAliasesPerNode = 16;
    /// <summary>Gets the hard alias ceiling for one entity.</summary>
    public const int MaximumAliasesPerNode = 32;
    /// <summary>Gets the maximum candidate identities considered in one bucket.</summary>
    public const int MaximumCandidateBucketSize = 64;
    /// <summary>Gets the hard number of source identities one optional entity suggestion may cite.</summary>
    public const int MaximumSuggestionSourceKeys = 16;
    /// <summary>Gets the default stable component or cascade node ceiling.</summary>
    public const int DefaultComponentNodes = 256;
    /// <summary>Gets the hard component or cascade node ceiling.</summary>
    public const int MaximumComponentNodes = 1_024;
    /// <summary>Gets the default stable component edge ceiling.</summary>
    public const int DefaultComponentEdges = 1_024;
    /// <summary>Gets the hard component edge ceiling.</summary>
    public const int MaximumComponentEdges = 4_096;
    /// <summary>Gets the stable traversal node ceiling.</summary>
    public const int MaximumStableTraversalNodes = 100;
    /// <summary>Gets the experimental traversal node ceiling.</summary>
    public const int MaximumExperimentalTraversalNodes = 500;
    /// <summary>Gets the stable graph traversal depth.</summary>
    public const int StableTraversalDepth = 1;
    /// <summary>Gets the experimental graph traversal depth.</summary>
    public const int MaximumExperimentalTraversalDepth = 2;
    /// <summary>Gets the maximum original Search seeds shared by contextual expansion.</summary>
    public const int MaximumSearchSeeds = 16;
    /// <summary>Gets the graph-only share of the contextual Search ceiling.</summary>
    public const int MaximumGraphSearchExpansions = 50;
    /// <summary>Gets the combined relationship and graph contextual Search ceiling.</summary>
    public const int MaximumContextualSearchExpansions = 100;
    /// <summary>Gets the maximum stable identifier length.</summary>
    public const int MaximumStableIdCharacters = 256;
    /// <summary>Gets the maximum retained canonical identity-input length.</summary>
    public const int MaximumCanonicalIdentityCharacters = 2_048;
    /// <summary>Gets the maximum display label length.</summary>
    public const int MaximumLabelCharacters = 256;
    /// <summary>Gets the maximum retained evidence explanation length.</summary>
    public const int MaximumEvidenceTextCharacters = 256;
    /// <summary>Gets the maximum bounded decision reason length.</summary>
    public const int MaximumDecisionReasonCharacters = 512;
    /// <summary>Gets the maximum optional suggestion count in one response.</summary>
    public const int MaximumSuggestions = 32;
    /// <summary>Gets the maximum attempts for an unchanged projection job.</summary>
    public const int MaximumRetryCount = 5;
    /// <summary>Gets the hard graph-worker concurrency ceiling.</summary>
    public const int MaximumWorkerConcurrency = 4;
    /// <summary>Gets the maximum schema-3 decisions mirrored by one completed legacy manifest.</summary>
    public const int MaximumLegacyDecisionMirrorRows = 100_000;
    /// <summary>Gets the smallest supported graph sidecar quota.</summary>
    public const long MinimumStorageQuotaBytes = 16L * 1024L * 1024L;
    /// <summary>Gets the largest supported graph sidecar quota.</summary>
    public const long MaximumStorageQuotaBytes = 16L * 1024L * 1024L * 1024L;
    /// <summary>Gets the durable worker heartbeat interval.</summary>
    public static TimeSpan HeartbeatInterval { get; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets the durable claim lease time-to-live.</summary>
    public static TimeSpan ClaimLeaseTimeToLive { get; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets the cooperative shutdown grace interval.</summary>
    public static TimeSpan ShutdownGracePeriod { get; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets the bounded interval between resource-eligibility probes.</summary>
    public static TimeSpan ResourceProbeInterval { get; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets the periodic safety-net interval for missed source or decision notifications.</summary>
    public static TimeSpan PeriodicReconciliationInterval { get; } = TimeSpan.FromMinutes(5);
}

/// <summary>Identifies one extensible graph node kind.</summary>
public readonly record struct GraphNodeKind
{
    /// <summary>Initializes a syntactically valid graph node code.</summary>
    /// <param name="value">Lowercase provider-neutral code.</param>
    public GraphNodeKind(string value) => Value = GraphCode.Validate(value, nameof(value));
    /// <summary>Gets the persisted provider-neutral code.</summary>
    public string Value { get; }
    /// <summary>Gets the stable File node kind.</summary>
    public static GraphNodeKind File { get; } = new("file");
    /// <summary>Gets the stable Source node kind.</summary>
    public static GraphNodeKind Source { get; } = new("source");
    /// <summary>Gets the stable Folder node kind.</summary>
    public static GraphNodeKind Folder { get; } = new("folder");
    /// <summary>Gets the stable Collection node kind.</summary>
    public static GraphNodeKind Collection { get; } = new("collection");
    /// <summary>Gets the stable exact-content Document Set node kind.</summary>
    public static GraphNodeKind DocumentSet { get; } = new("document-set");
    /// <summary>Gets the user-created Manual Entity node kind.</summary>
    public static GraphNodeKind ManualEntity { get; } = new("manual-entity");
    /// <summary>Gets whether this code is supported by the stable v2.0 graph.</summary>
    public bool IsStable => this == File || this == Source || this == Folder || this == Collection ||
        this == DocumentSet || this == ManualEntity;
    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one extensible graph edge kind.</summary>
public readonly record struct GraphEdgeKind
{
    /// <summary>Initializes a syntactically valid graph edge code.</summary>
    /// <param name="value">Lowercase provider-neutral code.</param>
    public GraphEdgeKind(string value) => Value = GraphCode.Validate(value, nameof(value));
    /// <summary>Gets the persisted provider-neutral code.</summary>
    public string Value { get; }
    /// <summary>Gets a projected evidence-backed file relationship.</summary>
    public static GraphEdgeKind RelatedFile { get; } = new("related-file");
    /// <summary>Gets the File-to-Source ownership edge.</summary>
    public static GraphEdgeKind OwnedBySource { get; } = new("owned-by-source");
    /// <summary>Gets the File-or-Folder to parent Folder edge.</summary>
    public static GraphEdgeKind LocatedInFolder { get; } = new("located-in-folder");
    /// <summary>Gets a membership edge.</summary>
    public static GraphEdgeKind MemberOf { get; } = new("member-of");
    /// <summary>Gets an exact-content Document Set edge.</summary>
    public static GraphEdgeKind SameDocumentSet { get; } = new("same-document-set");
    /// <summary>Gets an explicitly user-created edge.</summary>
    public static GraphEdgeKind Manual { get; } = new("manual");
    /// <summary>Gets whether this code is supported by the stable v2.0 graph.</summary>
    public bool IsStable => this == RelatedFile || this == OwnedBySource || this == LocatedInFolder ||
        this == MemberOf || this == SameDocumentSet || this == Manual;
    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies the retained authority behind one graph fact.</summary>
public readonly record struct GraphEvidenceKind
{
    /// <summary>Initializes a syntactically valid evidence code.</summary>
    /// <param name="value">Lowercase provider-neutral code.</param>
    public GraphEvidenceKind(string value) => Value = GraphCode.Validate(value, nameof(value));
    /// <summary>Gets the persisted provider-neutral code.</summary>
    public string Value { get; }
    /// <summary>Gets evidence based on an existing stable provider ID.</summary>
    public static GraphEvidenceKind StableIdentity { get; } = new("stable-identity");
    /// <summary>Gets source ownership evidence.</summary>
    public static GraphEvidenceKind SourceOwnership { get; } = new("source-ownership");
    /// <summary>Gets exact source-relative folder evidence.</summary>
    public static GraphEvidenceKind RelativeFolder { get; } = new("relative-folder");
    /// <summary>Gets validated exact content-hash evidence.</summary>
    public static GraphEvidenceKind ExactContentHash { get; } = new("exact-content-hash");
    /// <summary>Gets evidence from an authoritative v1.9 relationship row.</summary>
    public static GraphEvidenceKind LegacyRelationship { get; } = new("legacy-relationship");
    /// <summary>Gets evidence from authoritative v1.9 collection membership.</summary>
    public static GraphEvidenceKind CollectionMembership { get; } = new("collection-membership");
    /// <summary>Gets explicitly supplied manual evidence.</summary>
    public static GraphEvidenceKind Manual { get; } = new("manual");
    /// <summary>Gets whether the stable projection understands this evidence kind.</summary>
    public bool IsStable => this == StableIdentity || this == SourceOwnership || this == RelativeFolder ||
        this == ExactContentHash || this == LegacyRelationship || this == CollectionMembership || this == Manual;
    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Expresses deterministic confidence without implying probability.</summary>
public enum GraphConfidenceLevel
{
    /// <summary>One limited concrete evidence rule supports the fact.</summary>
    Low,
    /// <summary>Corroborating concrete evidence supports the fact.</summary>
    Medium,
    /// <summary>An exact strong rule supports the fact.</summary>
    High,
    /// <summary>The user explicitly confirmed or created the fact.</summary>
    Confirmed,
}

/// <summary>Identifies where a graph record originated.</summary>
public enum GraphOrigin
{
    /// <summary>The record is a deterministic mechanical projection.</summary>
    Mechanical,
    /// <summary>The record mirrors an authoritative v1.9 relationship.</summary>
    LegacyRelationship,
    /// <summary>The record mirrors an authoritative v1.9 Smart Collection.</summary>
    LegacyCollection,
    /// <summary>The record was explicitly created by the user.</summary>
    Manual,
    /// <summary>The record is an inactive or confirmed experimental suggestion.</summary>
    ExperimentalSuggestion,
}

/// <summary>Records durable run admission and user intent.</summary>
public enum GraphRunControlState
{
    /// <summary>Work exists but the coordinator has not started it.</summary>
    Pending,
    /// <summary>The coordinator may claim work.</summary>
    Running,
    /// <summary>New claims are stopped while active claims drain.</summary>
    PauseRequested,
    /// <summary>No work is currently claimed and resumable state is durable.</summary>
    Paused,
    /// <summary>Cancellation is requested while active claims drain.</summary>
    CancelRequested,
    /// <summary>Cancellation was durably acknowledged.</summary>
    Cancelled,
    /// <summary>The run reached a durable terminal completion.</summary>
    Complete,
}

/// <summary>Records the durable execution state of one logical projection job.</summary>
public enum GraphJobExecutionState
{
    /// <summary>The job may be claimed.</summary>
    Pending,
    /// <summary>One valid lease and fencing token owns the attempt.</summary>
    Running,
    /// <summary>The validated output was published.</summary>
    Complete,
    /// <summary>Cancellation was durably acknowledged.</summary>
    Cancelled,
    /// <summary>A classified transient failure may be retried.</summary>
    RetryableFailure,
    /// <summary>The same unchanged input cannot safely succeed.</summary>
    PermanentFailure,
    /// <summary>An optional provider required by this job is unavailable.</summary>
    WaitingForDependency,
    /// <summary>A resource or policy gate currently prevents work.</summary>
    WaitingForResources,
}

/// <summary>Records whether a published component matches its current inputs.</summary>
public enum GraphFreshnessState
{
    /// <summary>The component matches current authoritative inputs.</summary>
    Current,
    /// <summary>The prior valid component remains readable while replacement is pending.</summary>
    Stale,
}

/// <summary>Records whether a component passed graph integrity validation.</summary>
public enum GraphIntegrityState
{
    /// <summary>The component passed applicable integrity checks.</summary>
    Valid,
    /// <summary>Ambiguity or corruption requires selective repair.</summary>
    RepairRequired,
}

/// <summary>Combines the four independent durable state axes without collapsing their meaning.</summary>
public sealed record GraphStateVector(
    GraphRunControlState RunControl,
    GraphJobExecutionState JobExecution,
    GraphFreshnessState Freshness,
    GraphIntegrityState Integrity);

/// <summary>Reports whether a graph state or transition is valid.</summary>
public sealed record GraphStateValidationResult(bool IsValid, string ErrorCode, string Message)
{
    /// <summary>Creates a successful validation result.</summary>
    public static GraphStateValidationResult Valid() => new(true, string.Empty, string.Empty);
    /// <summary>Creates a failed validation result.</summary>
    /// <param name="errorCode">Stable diagnostic category.</param>
    /// <param name="message">Bounded actionable explanation.</param>
    public static GraphStateValidationResult Invalid(string errorCode, string message) => new(false, errorCode, message);
}

/// <summary>Declares path comparison semantics supplied by the authoritative source.</summary>
public enum GraphPathComparison
{
    /// <summary>Path keys compare using ordinal case-sensitive rules.</summary>
    CaseSensitive,
    /// <summary>Path keys compare using ordinal case-insensitive rules.</summary>
    CaseInsensitive,
}

internal static class GraphCode
{
    internal static string Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 64 || value[0] is < 'a' or > 'z')
        {
            throw new ArgumentOutOfRangeException(parameterName, "Graph codes must start with a lowercase ASCII letter and contain at most 64 characters.");
        }

        foreach (var character in value)
        {
            if (!(character is >= 'a' and <= 'z') && !(character is >= '0' and <= '9') && character is not '-' and not '.')
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Graph code contains unsupported character U+{(int)character:X4}."),
                    parameterName);
            }
        }

        return value;
    }
}
