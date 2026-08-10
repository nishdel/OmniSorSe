using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Describes whether graph sidecars may be opened without creating new local data.</summary>
public enum GraphStorageProvisioningState
{
    /// <summary>No graph sidecars exist and the feature remains disabled.</summary>
    Unprovisioned,
    /// <summary>Both graph stores exist and may be initialized.</summary>
    Provisioned,
    /// <summary>Partial or malformed provisioning requires explicit recovery.</summary>
    RepairRequired,
}

/// <summary>Reports graph projection availability independently from deep-index coverage.</summary>
public sealed record GraphProjectionCoverage(
    bool IsEnabled,
    bool IsAvailable,
    bool IsComplete,
    bool IsStale,
    long ProjectedObservationCount,
    long TotalObservationCount,
    long FailedCount,
    long WaitingCount,
    string? ManifestId,
    long ProjectionRevision,
    string Message)
{
    /// <summary>Gets the latest completely ingested source manifest.</summary>
    public string? IngestedManifestId { get; init; }
    /// <summary>Gets the latest completely ingested source revision.</summary>
    public long IngestedRevision { get; init; }
    /// <summary>Gets the latest completely applied source manifest.</summary>
    public string? AppliedManifestId { get; init; }
    /// <summary>Gets the latest completely applied source revision.</summary>
    public long AppliedRevision { get; init; }
    /// <summary>Gets the latest graph-native decision sequence durably ingested for reconciliation.</summary>
    public long IngestedDecisionSequence { get; init; }
    /// <summary>Gets the decision checkpoint durably ingested for reconciliation.</summary>
    public string? IngestedDecisionCheckpointId { get; init; }
    /// <summary>Gets the graph-native decision sequence applied to published graph data.</summary>
    public long AppliedDecisionSequence { get; init; }
    /// <summary>Gets the decision checkpoint applied to published graph data.</summary>
    public string? AppliedDecisionCheckpointId { get; init; }
    /// <summary>Gets the latest authoritative privacy sequence durably ingested for reconciliation.</summary>
    public long IngestedPrivacySequence { get; init; }
    /// <summary>Gets the authoritative source privacy sequence applied to published graph data.</summary>
    public long AppliedPrivacySequence { get; init; }
}

/// <summary>Reports durable graph lifecycle and bounded operational counts.</summary>
public sealed record GraphCoordinatorStatus
{
    /// <summary>Gets whether graph sidecars have been explicitly provisioned.</summary>
    public bool IsProvisioned { get; init; }
    /// <summary>Gets whether graph processing and queries are enabled.</summary>
    public bool IsEnabled { get; init; }
    /// <summary>Gets the active run ID, when one exists.</summary>
    public string? RunId { get; init; }
    /// <summary>Gets the current coordinator fencing epoch.</summary>
    public long FencingEpoch { get; init; }
    /// <summary>Gets the durable run-control state.</summary>
    public GraphRunControlState RunControl { get; init; } = GraphRunControlState.Pending;
    /// <summary>Gets the currently active job state, when one exists.</summary>
    public GraphJobExecutionState? ActiveJobState { get; init; }
    /// <summary>Gets aggregate published-component freshness.</summary>
    public GraphFreshnessState Freshness { get; init; } = GraphFreshnessState.Current;
    /// <summary>Gets aggregate graph integrity.</summary>
    public GraphIntegrityState Integrity { get; init; } = GraphIntegrityState.Valid;
    /// <summary>Gets the current durable projection stage code.</summary>
    public string? CurrentStage { get; init; }
    /// <summary>Gets whether the input manifest was completely captured.</summary>
    public bool InputManifestComplete { get; init; }
    /// <summary>Gets the number of pending jobs.</summary>
    public long PendingCount { get; init; }
    /// <summary>Gets the number of running jobs.</summary>
    public long RunningCount { get; init; }
    /// <summary>Gets the number of completed jobs.</summary>
    public long CompletedCount { get; init; }
    /// <summary>Gets the number of retryable failures.</summary>
    public long RetryableFailureCount { get; init; }
    /// <summary>Gets the number of permanent failures.</summary>
    public long PermanentFailureCount { get; init; }
    /// <summary>Gets the number of cancelled jobs.</summary>
    public long CancelledCount { get; init; }
    /// <summary>Gets the number of jobs waiting for a dependency or resource.</summary>
    public long WaitingCount { get; init; }
    /// <summary>Gets the number of observations durably processed by this run.</summary>
    public long ProcessedObservationCount { get; init; }
    /// <summary>Gets the completed-manifest observation total.</summary>
    public long TotalObservationCount { get; init; }
    /// <summary>Gets the non-negative number of observations remaining.</summary>
    public long RemainingObservationCount { get; init; }
    /// <summary>Gets current graph storage size in bytes.</summary>
    public long StorageSizeBytes { get; init; }
    /// <summary>Gets the configured graph storage ceiling in bytes, or zero when no explicit limit exists.</summary>
    public long MaximumStorageSizeBytes { get; init; }
    /// <summary>Gets the verified provider-neutral sidecar storage breakdown.</summary>
    public GraphStorageBreakdown StorageBreakdown { get; init; } = GraphStorageBreakdown.Empty;
    /// <summary>Gets the latest bounded graph-maintenance state.</summary>
    public GraphMaintenanceStatus Maintenance { get; init; } = GraphMaintenanceStatus.Idle;
    /// <summary>Gets estimated remaining duration only after enough stable samples exist.</summary>
    public TimeSpan? EstimatedRemaining { get; init; }
    /// <summary>Gets the privacy-safe current work label.</summary>
    public string? CurrentWorkLabel { get; init; }
    /// <summary>Gets the current projection coverage.</summary>
    public required GraphProjectionCoverage Coverage { get; init; }
    /// <summary>Gets a bounded lifecycle message.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>Contains the result of recovering expired or interrupted graph work.</summary>
public sealed record GraphRecoveryResult(int RecoveredClaimCount, int RepairRequiredCount, string Message);

/// <summary>Identifies one durable projection run and its fencing epoch.</summary>
public sealed record GraphProjectionRun(
    string RunId,
    long FencingEpoch,
    GraphProjectionSnapshot Snapshot,
    GraphRunControlState State,
    bool InputManifestComplete);

/// <summary>Identifies one durable, ordered graph projection stage.</summary>
public enum GraphProjectionStage
{
    /// <summary>The immutable source observation was durably captured.</summary>
    ObservationCaptured,
    /// <summary>Bounded candidates were extracted from retained indexed fields.</summary>
    CandidatesExtracted,
    /// <summary>Candidate text, types, timestamps, and bounds were normalized and validated.</summary>
    CandidatesNormalized,
    /// <summary>Deterministic identity plus authoritative decisions were resolved.</summary>
    IdentityResolved,
    /// <summary>Bounded edges, facts, and their evidence references were prepared.</summary>
    EdgesPrepared,
    /// <summary>The complete replacement component passed integrity and authority-independent validation.</summary>
    ComponentValidated,
    /// <summary>The fenced replacement component and applied watermark were atomically published.</summary>
    ComponentPublished,
    /// <summary>Superseded rows and bounded operational history were safely retired.</summary>
    StaleRowsCleaned,
}

/// <summary>Contains one durable unit of projection work.</summary>
public sealed record GraphProjectionWorkItem(
    string WorkId,
    string RunId,
    GraphProjectionObservation Observation,
    GraphStateVector State,
    int Attempt)
{
    /// <summary>Gets the last durably completed stage.</summary>
    public GraphProjectionStage Stage { get; init; } = GraphProjectionStage.ObservationCaptured;
    /// <summary>Gets the bounded fingerprint for the last completed pure stage.</summary>
    public string? StageInputFingerprint { get; init; }
}

/// <summary>Contains exclusive ownership of one projection attempt.</summary>
public sealed record GraphProjectionClaim(
    GraphProjectionWorkItem WorkItem,
    string ClaimToken,
    string OwnerInstanceId,
    long FencingEpoch,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset LeaseExpiresAtUtc);

/// <summary>Requests one monotonic, fenced pure-stage checkpoint.</summary>
public sealed record GraphProjectionStageTransition(
    GraphProjectionStage ExpectedStage,
    GraphProjectionStage CompletedStage,
    string InputFingerprint);

/// <summary>Classifies one durable projection failure without retaining private source content.</summary>
public sealed record GraphProjectionFailure(
    string Category,
    string ErrorCode,
    bool Retryable,
    string Message);

/// <summary>Describes a requested durable run-control transition.</summary>
public sealed record GraphRunControlRequest(
    string? RunId,
    GraphRunControlState RequestedState,
    string ReasonCode,
    DateTimeOffset RequestedAtUtc);

/// <summary>Updates the durable privacy-safe stage shown for one projection run.</summary>
public sealed record GraphRunStageUpdate(
    string StageCode,
    string? CurrentWorkLabel);

/// <summary>Reports sidecar bytes without exposing provider paths.</summary>
public sealed record GraphStorageBreakdown
{
    /// <summary>Gets active derived graph-store bytes, including journal sidecars.</summary>
    public long DerivedStoreBytes { get; init; }
    /// <summary>Gets authoritative decision-ledger bytes, including journal sidecars.</summary>
    public long DecisionLedgerBytes { get; init; }
    /// <summary>Gets verified graph backup bytes retained by the lifecycle provider.</summary>
    public long VerifiedBackupBytes { get; init; }
    /// <summary>Gets the verified total across graph-owned stores and backups.</summary>
    public long TotalBytes { get; init; }
    /// <summary>Gets the configured total storage ceiling, or zero when unset.</summary>
    public long MaximumBytes { get; init; }
    /// <summary>Gets storage reserved for safe migration, recovery, and compaction.</summary>
    public long RequiredReserveBytes { get; init; }
    /// <summary>Gets whether provider file inventory and backup membership were verified.</summary>
    public bool IsInventoryVerified { get; init; }

    /// <summary>Gets an empty, verified breakdown for an unprovisioned graph.</summary>
    public static GraphStorageBreakdown Empty { get; } = new() { IsInventoryVerified = true };
}

/// <summary>Identifies why one bounded maintenance pass was requested.</summary>
public enum GraphMaintenanceTrigger
{
    /// <summary>A user explicitly requested reviewed maintenance.</summary>
    UserRequested,
    /// <summary>The background runtime observed verified quota pressure.</summary>
    AutomaticQuotaPressure,
    /// <summary>A selective repair operation requested safe cleanup.</summary>
    Repair,
}

/// <summary>Requests one bounded, cancellable graph-only maintenance pass.</summary>
public sealed record GraphMaintenanceRequest(
    long MaximumStorageSizeBytes,
    GraphMaintenanceTrigger Trigger,
    bool AllowCompaction = true);

/// <summary>Reports the durable result of one graph maintenance pass.</summary>
public sealed record GraphMaintenanceResult(
    int RecordsRemoved,
    long BytesBefore,
    long BytesAfter,
    bool QuotaBlocked,
    DateTimeOffset CompletedAtUtc,
    string Message);

/// <summary>Reports the latest privacy-safe graph-maintenance state.</summary>
public sealed record GraphMaintenanceStatus(
    bool IsRunning,
    bool QuotaBlocked,
    DateTimeOffset? LastCompletedAtUtc,
    int LastRecordsRemoved,
    string Message)
{
    /// <summary>Gets the default state before maintenance has run.</summary>
    public static GraphMaintenanceStatus Idle { get; } = new(false, false, null, 0, string.Empty);
}

/// <summary>Describes the current privacy and legacy-decision authority for a graph operation.</summary>
public sealed record GraphAuthoritySnapshot(
    bool IsAvailable,
    bool IsAllowed,
    long PrivacySequence,
    string LegacyDecisionManifestId,
    string ReasonCode)
{
    /// <summary>Gets the latest completed authoritative source manifest.</summary>
    public string? CurrentSourceManifestId { get; init; }
    /// <summary>Gets the latest completed authoritative source revision.</summary>
    public long CurrentSourceRevision { get; init; }
}

/// <summary>Requests current authority validation before a graph read or mutation.</summary>
public sealed record GraphAuthorityRequest(
    IReadOnlyList<string> StableKeys,
    string OperationCode);

/// <summary>Contains one opaque query continuation token.</summary>
public sealed record GraphPageCursor(string Value);

/// <summary>Contains one bounded immutable page.</summary>
/// <typeparam name="T">Provider-neutral row type.</typeparam>
public sealed record GraphPage<T>(IReadOnlyList<T> Items, GraphPageCursor? NextCursor, long? TotalCount);

/// <summary>Filters a paged node query without exposing provider syntax.</summary>
public sealed record GraphNodeQuery(
    GraphNodeKind? Kind = null,
    string? NormalizedLabelPrefix = null,
    GraphFreshnessState? Freshness = null,
    GraphIntegrityState? Integrity = null,
    GraphPageCursor? Cursor = null,
    int PageSize = GraphLimits.DefaultPageSize);

/// <summary>Filters one bounded evidence-backed fact page for a node.</summary>
public sealed record GraphFactQuery(
    string NodeId,
    GraphFactKind? Kind = null,
    GraphPageCursor? Cursor = null,
    int PageSize = GraphLimits.DefaultPageSize);

/// <summary>Filters one bounded timestamp-fact timeline for a node.</summary>
public sealed record GraphTimelineQuery(
    string NodeId,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    GraphPageCursor? Cursor = null,
    int PageSize = GraphLimits.DefaultPageSize);

/// <summary>Contains one evidence-backed timeline event derived from a retained timestamp fact.</summary>
public sealed record GraphTimelineEntry(
    string FactId,
    string SubjectNodeId,
    GraphFactKind Kind,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<string> EvidenceIds,
    string AlgorithmVersion);

/// <summary>Filters a bounded direct-neighbor query.</summary>
public sealed record GraphNeighborQuery(
    string NodeId,
    GraphEdgeKind? EdgeKind = null,
    GraphNodeKind? NeighborKind = null,
    GraphPageCursor? Cursor = null,
    int PageSize = GraphLimits.DefaultPageSize,
    int Depth = GraphLimits.StableTraversalDepth,
    bool ExperimentalTraversal = false);

/// <summary>Contains an inspectable node with bounded aliases and counts.</summary>
public sealed record GraphNodeDetails(
    GraphNode Node,
    IReadOnlyList<string> Aliases,
    int IncomingEdgeCount,
    int OutgoingEdgeCount);

/// <summary>Contains one direct neighbor and the actual connecting edge.</summary>
public sealed record GraphNeighbor(
    GraphNode Node,
    GraphEdge Edge,
    IReadOnlyList<GraphEvidenceReference> Evidence);

/// <summary>Requests optional bounded graph context for already-ranked Search seeds.</summary>
public sealed record GraphSearchRequest(
    IReadOnlyList<string> SeedFileIds,
    int MaximumExpansions = GraphLimits.MaximumGraphSearchExpansions);

/// <summary>Contains one explainable graph Search expansion.</summary>
public sealed record GraphSearchExpansion(
    string SeedFileId,
    string RelatedFileId,
    string EdgeId,
    GraphEdgeKind EdgeKind,
    GraphConfidenceLevel Confidence,
    string Explanation,
    long ProjectionRevision,
    GraphFreshnessState Freshness);

/// <summary>Contains bounded graph expansions and separate projection coverage.</summary>
public sealed record GraphSearchResult(
    IReadOnlyList<GraphSearchExpansion> Expansions,
    GraphProjectionCoverage Coverage,
    bool IsAvailable,
    string Message);

/// <summary>Identifies a graph-native manual decision.</summary>
public enum GraphDecisionKind
{
    /// <summary>Create a stable manual entity.</summary>
    CreateManualEntity,
    /// <summary>Rename a manual entity.</summary>
    RenameManualEntity,
    /// <summary>Add a bounded alias to a manual entity.</summary>
    AddAlias,
    /// <summary>Remove an alias from a manual entity.</summary>
    RemoveAlias,
    /// <summary>Merge compatible manual or confirmed experimental entities.</summary>
    MergeEntities,
    /// <summary>Split compatible manual or confirmed experimental entities.</summary>
    SplitEntities,
    /// <summary>Create a manual edge.</summary>
    LinkNodes,
    /// <summary>Remove a manual or automatic edge.</summary>
    UnlinkNodes,
    /// <summary>Reject an inactive suggestion.</summary>
    RejectSuggestion,
    /// <summary>Prevent a specific compatible identity merge.</summary>
    NeverMerge,
    /// <summary>Forget graph-owned data for one scope.</summary>
    Forget,
    /// <summary>Exclude one scope from future graph projection.</summary>
    Exclude,
    /// <summary>Remove a graph-native projection exclusion.</summary>
    Include,
}

/// <summary>Contains one validated graph-native manual decision command.</summary>
public sealed record GraphDecisionCommand
{
    /// <summary>Gets the command kind.</summary>
    public required GraphDecisionKind Kind { get; init; }
    /// <summary>Gets the primary stable subject key.</summary>
    public required string SubjectId { get; init; }
    /// <summary>Gets the optional target key.</summary>
    public string? TargetId { get; init; }
    /// <summary>Gets the optional bounded display label or alias.</summary>
    public string? Label { get; init; }
    /// <summary>Gets an optional node kind for manual creation.</summary>
    public GraphNodeKind? NodeKind { get; init; }
    /// <summary>Gets a bounded reason code or user-visible reason.</summary>
    public string? Reason { get; init; }
    /// <summary>Gets the captured relationship source endpoint for durable unlink suppression.</summary>
    public string? RelationshipSourceNodeId { get; init; }
    /// <summary>Gets the captured relationship target endpoint for durable unlink suppression.</summary>
    public string? RelationshipTargetNodeId { get; init; }
    /// <summary>Gets the captured relationship kind so later algorithm versions cannot recreate a rejected pair.</summary>
    public GraphEdgeKind? RelationshipKind { get; init; }
    /// <summary>Gets the bounded ownership scope in which relationship suppression applies.</summary>
    public string? RelationshipScope { get; init; }
    /// <summary>Gets the expected authoritative sequence for optimistic concurrency.</summary>
    public long ExpectedSequence { get; init; }
    /// <summary>Gets the authoritative control-settings revision that must still be enabled when appended.</summary>
    public long ExpectedControlSettingsRevision { get; init; }
}

/// <summary>Contains one append-only authoritative graph-native decision.</summary>
public sealed record GraphDecisionEntry(
    string DecisionId,
    long Sequence,
    GraphDecisionCommand Command,
    DateTimeOffset CreatedAtUtc,
    string CanonicalHash);

/// <summary>Contains a validated graph-native decision checkpoint.</summary>
public sealed record GraphDecisionSnapshot(
    long Sequence,
    string CheckpointId,
    string CanonicalHash,
    bool IsValid);

/// <summary>Describes one privacy-safe managed decision-ledger recovery point.</summary>
public sealed record GraphDecisionRecoveryPoint(
    string RecoveryPointId,
    long DecisionSequence,
    long PrivacySequence,
    long StoreGeneration,
    DateTimeOffset CommittedAtUtc,
    bool IsPinned,
    bool IsRestorable,
    string StatusCode);

/// <summary>Contains authoritative graph opt-in and bounded background resource settings.</summary>
public sealed record GraphControlSettings
{
    /// <summary>Gets whether graph projection and query are explicitly enabled.</summary>
    public bool IsEnabled { get; init; }
    /// <summary>Gets whether the local privacy consent was explicitly confirmed.</summary>
    public bool ConsentConfirmed { get; init; }
    /// <summary>Gets the shared desktop background resource mode.</summary>
    public IndexingResourceMode ResourceMode { get; init; } = IndexingResourceMode.Balanced;
    /// <summary>Gets the bounded graph worker concurrency derived from the selected resource mode.</summary>
    public int MaximumConcurrency { get; init; } = 2;
    /// <summary>Gets whether graph processing is restricted to idle periods when supported.</summary>
    public bool ProcessOnlyWhileIdle { get; init; }
    /// <summary>Gets whether graph processing is restricted to external power when supported.</summary>
    public bool ProcessOnlyWhileConnectedToPower { get; init; }
    /// <summary>Gets the optional battery threshold.</summary>
    public int? PauseBelowBatteryPercentage { get; init; }
    /// <summary>Gets the optional inclusive local processing-window start hour.</summary>
    public int? ProcessingWindowStartHour { get; init; }
    /// <summary>Gets the optional exclusive local processing-window end hour.</summary>
    public int? ProcessingWindowEndHour { get; init; }
    /// <summary>Gets the monotonic settings revision.</summary>
    public long Revision { get; init; }
}

/// <summary>Updates user-controllable graph resource policy without changing privacy consent.</summary>
public sealed record GraphResourceControlUpdate
{
    /// <summary>Gets the selected shared resource mode.</summary>
    public required IndexingResourceMode ResourceMode { get; init; }
    /// <summary>Gets whether processing should wait for an idle system when supported.</summary>
    public bool ProcessOnlyWhileIdle { get; init; }
    /// <summary>Gets whether processing should wait for external power when supported.</summary>
    public bool ProcessOnlyWhileConnectedToPower { get; init; }
    /// <summary>Gets the optional battery pause threshold.</summary>
    public int? PauseBelowBatteryPercentage { get; init; }
    /// <summary>Gets the optional inclusive local processing-window start hour.</summary>
    public int? ProcessingWindowStartHour { get; init; }
    /// <summary>Gets the optional exclusive local processing-window end hour.</summary>
    public int? ProcessingWindowEndHour { get; init; }
    /// <summary>Gets the expected authoritative settings revision.</summary>
    public required long ExpectedRevision { get; init; }
}

/// <summary>Reports whether current resource policy permits one bounded graph unit.</summary>
public sealed record GraphResourceEligibility(bool MayProcess, string? WaitingReason);

/// <summary>Contains the deterministic visible mutation produced by one graph-native decision.</summary>
public sealed record GraphDecisionProjection
{
    /// <summary>Gets the authoritative decision entry.</summary>
    public required GraphDecisionEntry Decision { get; init; }
    /// <summary>Gets a manual entity replacement for create decisions.</summary>
    public GraphNode? Node { get; init; }
    /// <summary>Gets an alias replacement for add-alias decisions.</summary>
    public GraphAlias? Alias { get; init; }
    /// <summary>Gets a manual edge replacement for link decisions.</summary>
    public GraphEdge? Edge { get; init; }
    /// <summary>Gets a bounded replacement label for rename decisions.</summary>
    public string? ReplacementLabel { get; init; }
    /// <summary>Gets the stable subject to suppress, retire, merge, or split.</summary>
    public required string SubjectId { get; init; }
    /// <summary>Gets an optional second stable identity.</summary>
    public string? TargetId { get; init; }
}

/// <summary>Contains a controlled provider-neutral operation outcome.</summary>
public sealed record GraphOperationResult(bool Succeeded, string Message, int AffectedCount, bool RequiresRestart = false);

/// <summary>Identifies the scope of graph privacy inspection or forgetting.</summary>
public enum GraphPrivacyScopeKind
{
    /// <summary>All graph-owned data.</summary>
    All,
    /// <summary>One existing indexed file.</summary>
    File,
    /// <summary>One existing indexed source.</summary>
    Source,
    /// <summary>One graph node.</summary>
    Node,
    /// <summary>One existing Smart Collection.</summary>
    Collection,
}

/// <summary>Identifies a graph-only privacy action.</summary>
public enum GraphPrivacyAction
{
    /// <summary>Forget graph-derived data while retaining graph-native decisions.</summary>
    ForgetDerivedData,
    /// <summary>Exclude a scope from future graph projection.</summary>
    ExcludeFromProjection,
    /// <summary>Remove a graph-only exclusion.</summary>
    IncludeInProjection,
    /// <summary>Clear all derived graph data while preserving decisions.</summary>
    ClearAllDerivedData,
    /// <summary>Irreversibly clear graph-native decisions after explicit confirmation.</summary>
    ClearAllDecisions,
}

/// <summary>Identifies one graph privacy scope.</summary>
public sealed record GraphPrivacyScope(GraphPrivacyScopeKind Kind, string StableId);

/// <summary>Contains privacy-safe information retained for one graph scope.</summary>
public sealed record GraphPrivacyInspection(
    GraphPrivacyScope Scope,
    int NodeCount,
    int EdgeCount,
    int EvidenceCount,
    int AliasCount,
    int DecisionCount,
    bool IsExcluded,
    DateTimeOffset? LastProjectedAtUtc,
    string Message);

/// <summary>Contains one explicit graph-only privacy mutation.</summary>
public sealed record GraphPrivacyChange(
    GraphPrivacyScope Scope,
    GraphPrivacyAction Action,
    bool ConfirmSourceFilesUnaffected,
    string? ConfirmationText = null);

/// <summary>Identifies a selective graph repair action.</summary>
public enum GraphRepairKind
{
    /// <summary>Verify bounded graph invariants without changing valid records.</summary>
    Verify,
    /// <summary>Reproject one affected component.</summary>
    ReprojectComponent,
    /// <summary>Reproject data owned by one indexed file.</summary>
    ReprojectFile,
    /// <summary>Reproject data owned by one indexed source.</summary>
    ReprojectSource,
    /// <summary>Reconcile the non-authoritative v1.9 decision mirror.</summary>
    ReconcileLegacyDecisions,
    /// <summary>Repair missing or stale evidence references.</summary>
    RepairEvidence,
    /// <summary>Remove verified orphaned derived graph records.</summary>
    RemoveOrphans,
    /// <summary>Rebuild all derived graph data while preserving decisions.</summary>
    RebuildDerivedGraph,
}

/// <summary>Requests a bounded, cancellable graph verification or repair.</summary>
public sealed record GraphRepairRequest(
    GraphRepairKind Kind,
    string? StableId = null,
    bool ConfirmSourceFilesUnaffected = false);

/// <summary>Reports privacy-safe graph diagnostics.</summary>
public sealed record GraphDiagnosticsSnapshot
{
    /// <summary>Gets the validated graph schema version without exposing provider DDL.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets the bounded provider code without a database path.</summary>
    public string ProviderCode { get; init; } = "provider-neutral";
    /// <summary>Gets bounded active projection algorithm version codes.</summary>
    public IReadOnlyList<string> AlgorithmVersions { get; init; } = [];
    /// <summary>Gets bounded per-stage duration aggregates without work labels or source paths.</summary>
    public IReadOnlyList<GraphStageDurationAggregate> StageDurations { get; init; } = [];
    /// <summary>Gets redacted operational-history counts.</summary>
    public GraphOperationalHistorySummary OperationalHistory { get; init; } = new();
    /// <summary>Gets the active run ID.</summary>
    public string? RunId { get; init; }
    /// <summary>Gets the active projection revision.</summary>
    public long ProjectionRevision { get; init; }
    /// <summary>Gets active node count.</summary>
    public long NodeCount { get; init; }
    /// <summary>Gets active edge count.</summary>
    public long EdgeCount { get; init; }
    /// <summary>Gets retained evidence count.</summary>
    public long EvidenceCount { get; init; }
    /// <summary>Gets graph-native decision count.</summary>
    public long DecisionCount { get; init; }
    /// <summary>Gets components requiring repair.</summary>
    public long RepairRequiredCount { get; init; }
    /// <summary>Gets expired claims recovered since initialization.</summary>
    public long RecoveredClaimCount { get; init; }
    /// <summary>Gets the current queue length.</summary>
    public long QueueLength { get; init; }
    /// <summary>Gets a bounded failure category, not exception text or file content.</summary>
    public string? LastFailureCategory { get; init; }
    /// <summary>Gets the verified graph-owned sidecar storage breakdown.</summary>
    public GraphStorageBreakdown StorageBreakdown { get; init; } = GraphStorageBreakdown.Empty;
    /// <summary>Gets the latest bounded graph-maintenance state.</summary>
    public GraphMaintenanceStatus Maintenance { get; init; } = GraphMaintenanceStatus.Idle;
    /// <summary>Gets current graph coverage.</summary>
    public required GraphProjectionCoverage Coverage { get; init; }
}

/// <summary>Aggregates one privacy-safe projection-stage duration.</summary>
public sealed record GraphStageDurationAggregate(
    string StageCode,
    long InvocationCount,
    TimeSpan TotalDuration,
    TimeSpan MaximumDuration);

/// <summary>Summarizes redacted safety, cancellation, recovery, and repair history.</summary>
public sealed record GraphOperationalHistorySummary
{
    /// <summary>Gets the number of provider-enforced bound events.</summary>
    public long BoundEventCount { get; init; }
    /// <summary>Gets the number of storage-quota events.</summary>
    public long QuotaEventCount { get; init; }
    /// <summary>Gets the number of durably acknowledged cancellations.</summary>
    public long CancellationCount { get; init; }
    /// <summary>Gets the number of recovered interrupted claims or runs.</summary>
    public long RecoveryCount { get; init; }
    /// <summary>Gets the number of bounded verification or repair operations.</summary>
    public long RepairCount { get; init; }
    /// <summary>Gets the latest operational event time, if history exists.</summary>
    public DateTimeOffset? LastEventAtUtc { get; init; }
}

/// <summary>Identifies a confirmation-required experimental entity suggestion type.</summary>
public enum GraphSuggestionKind
{
    /// <summary>A possible project.</summary>
    Project,
    /// <summary>A possible organization.</summary>
    Organization,
    /// <summary>A possible purchase.</summary>
    Purchase,
    /// <summary>A possible trip.</summary>
    Trip,
    /// <summary>A possible person.</summary>
    Person,
    /// <summary>A possible place.</summary>
    Place,
    /// <summary>A possible event.</summary>
    Event,
    /// <summary>A possible topic.</summary>
    Topic,
}

/// <summary>Contains one untrusted optional suggestion before strict validation.</summary>
public sealed record GraphSuggestionCandidate(
    GraphSuggestionKind Kind,
    string Scope,
    string Label,
    IReadOnlyList<string> SourceStableKeys,
    IReadOnlyList<string> EvidenceStableKeys,
    string ProviderVersion);

/// <summary>Contains a validated inactive suggestion that never establishes identity by itself.</summary>
public sealed record GraphValidatedSuggestion(
    string SuggestionId,
    GraphSuggestionKind Kind,
    string Scope,
    string Label,
    IReadOnlyList<string> SourceStableKeys,
    IReadOnlyList<string> EvidenceStableKeys,
    string ProviderVersion,
    bool RequiresConfirmation);

/// <summary>Controls optional suggestion validation; suggestions are disabled by default.</summary>
public sealed record GraphSuggestionOptions(
    bool Enabled = false,
    int MaximumSuggestions = GraphLimits.MaximumSuggestions,
    int MaximumSourceKeysPerSuggestion = 16,
    int MaximumEvidenceKeysPerSuggestion = GraphLimits.MaximumEvidencePerEdge);
