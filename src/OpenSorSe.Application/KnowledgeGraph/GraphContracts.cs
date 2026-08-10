namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Controls graph sidecar provisioning before either provider store is opened.</summary>
public interface IGraphStorageLifecycle : IAsyncDisposable
{
    /// <summary>Inspects sidecar lifecycle state without creating files or directories.</summary>
    Task<GraphStorageProvisioningState> GetProvisioningStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically provisions both graph stores after explicit consent.</summary>
    Task ProvisionAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a non-mutating verified inventory of graph-owned sidecar storage.</summary>
    Task<GraphStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provides explicit replacement of a rebuildable derived graph store without resetting decisions.</summary>
public interface IGraphDerivedStoreRecoveryProvider
{
    /// <summary>Quarantines an invalid derived store and publishes a validated empty replacement after review.</summary>
    Task<GraphOperationResult> RecoverDerivedStoreAsync(
        string confirmationText,
        CancellationToken cancellationToken = default);
}

/// <summary>Exposes completed, provider-consistent v1.9 observations without storage-specific APIs.</summary>
public interface IGraphProjectionSource : IAsyncDisposable
{
    /// <summary>Opens the latest completed immutable source and legacy-decision manifest.</summary>
    Task<GraphProjectionSnapshot> OpenCompletedSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one stable-primary-key page from an immutable completed snapshot.</summary>
    Task<GraphProjectionPage> ReadPageAsync(
        GraphProjectionSnapshot snapshot,
        GraphProjectionCursor? cursor,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Validates current privacy and legacy-decision authority at point of use.</summary>
    Task<GraphAuthoritySnapshot> ValidateAuthorityAsync(
        GraphAuthorityRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves conservative stable graph identities independently from persistence and UI.</summary>
public interface IGraphIdentityResolver
{
    /// <summary>Resolves one stable mechanical or manual identity and retains collision inputs.</summary>
    GraphIdentityResolution Resolve(GraphIdentityInput input);
}

/// <summary>Validates individual four-axis state vectors and their legal transitions.</summary>
public interface IGraphStateValidator
{
    /// <summary>Validates one complete state vector.</summary>
    GraphStateValidationResult Validate(GraphStateVector state);

    /// <summary>Validates a transition without collapsing independent state axes.</summary>
    GraphStateValidationResult ValidateTransition(GraphStateVector previous, GraphStateVector next);
}

/// <summary>Builds deterministic bounded graph replacements from retained provider observations.</summary>
public interface IGraphProjectionBuilder
{
    /// <summary>Builds one bounded component without opening source files or calling external dependencies.</summary>
    GraphComponentProjection Build(
        GraphProjectionObservation observation,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Projects graph-native decisions into deterministic visible mutations.</summary>
public interface IGraphDecisionProjectionBuilder
{
    /// <summary>Builds one bounded visible mutation without reading source files.</summary>
    GraphDecisionProjection Build(
        GraphDecisionEntry decision,
        GraphDecisionSnapshot snapshot,
        DateTimeOffset validatedAtUtc);
}

/// <summary>
/// Commits graph-initiated corrections for v1.9-owned relationships and Smart Collection
/// memberships through their existing authoritative service before graph reconciliation.
/// </summary>
public interface IGraphLegacyAuthorityBridge
{
    /// <summary>Removes one authoritative v1.9 relationship identified by its exact file pair.</summary>
    Task<GraphOperationResult> UnlinkRelationshipAsync(
        string firstFileId,
        string secondFileId,
        bool preventRegeneration,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one authoritative v1.9 Smart Collection membership and retains its exclusion.</summary>
    Task<GraphOperationResult> SplitCollectionMemberAsync(
        string collectionId,
        string fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>Evaluates graph background resource admission without platform-specific coupling.</summary>
public interface IGraphResourceAdmissionPolicy
{
    /// <summary>Returns current bounded resource eligibility for authoritative graph settings.</summary>
    Task<GraphResourceEligibility> GetEligibilityAsync(
        GraphControlSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>Schedules bounded resource-eligibility probes without polling or spin waits.</summary>
public interface IGraphResourceProbeScheduler
{
    /// <summary>Waits until the next configured eligibility probe.</summary>
    Task WaitForNextProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Schedules claim heartbeats independently from storage and wall-clock implementations.</summary>
public interface IGraphClaimHeartbeatScheduler
{
    /// <summary>Waits until the next bounded lease-renewal boundary.</summary>
    Task WaitForHeartbeatAsync(CancellationToken cancellationToken = default);
}

/// <summary>Schedules a low-frequency safety net for missed source or decision notifications.</summary>
public interface IGraphPeriodicReconciliationScheduler
{
    /// <summary>Waits until the next periodic reconciliation boundary.</summary>
    Task WaitForNextReconciliationAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persists graph projection lifecycle, immutable components, and bounded queries.</summary>
public interface IGraphStore : IAsyncDisposable
{
    /// <summary>Initializes the graph store and validates schema and integrity metadata.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns current durable lifecycle and coverage state.</summary>
    Task<GraphCoordinatorStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Recovers expired claims and stale run ownership using fencing.</summary>
    Task<GraphRecoveryResult> RecoverAsync(
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Enables or disables graph work and reads without deleting graph-owned data.</summary>
    Task<GraphOperationResult> SetEnabledAsync(
        bool enabled,
        bool consentConfirmed,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Durably records a resource-waiting scheduler gate without claiming work.</summary>
    Task<GraphOperationResult> SetResourceWaitingAsync(
        GraphProjectionRun run,
        string reasonCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Durably records one bounded privacy-safe run stage for progress reporting.</summary>
    Task SetRunStageAsync(
        GraphProjectionRun run,
        GraphRunStageUpdate update,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Runs one bounded retention and optional compaction pass at a safe coordinator boundary.</summary>
    Task<GraphMaintenanceResult> MaintainAsync(
        GraphMaintenanceRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Begins or resumes projection for one completed immutable source snapshot.</summary>
    Task<GraphProjectionRun> BeginProjectionAsync(
        GraphProjectionSnapshot snapshot,
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Durably queues one source page idempotently.</summary>
    Task QueueProjectionPageAsync(
        GraphProjectionRun run,
        GraphProjectionPage page,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a fully enumerated source manifest complete after validating its terminal hash and count.</summary>
    Task CompleteInputManifestAsync(
        GraphProjectionRun run,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Stages one ordered graph-native decision page for atomic publication with the run.</summary>
    Task ApplyDecisionProjectionPageAsync(
        GraphProjectionRun run,
        GraphDecisionSnapshot decisionSnapshot,
        IReadOnlyList<GraphDecisionProjection> projections,
        bool isLastPage,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the next eligible job for one bounded lease. Providers return claims in
    /// deterministic dependency-safe phase order and do not cross into a higher phase while a
    /// lower phase is pending or running: Source/LegacyDecision, File/Collection,
    /// Relationship/CollectionMembership, then Deletion.
    /// </summary>
    Task<GraphProjectionClaim?> TryClaimNextAsync(
        GraphProjectionRun run,
        string ownerInstanceId,
        DateTimeOffset nowUtc,
        TimeSpan leaseTimeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>Renews one still-valid claim without publishing output.</summary>
    Task<bool> RenewClaimAsync(
        GraphProjectionClaim claim,
        DateTimeOffset nowUtc,
        TimeSpan leaseTimeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically advances one still-owned claim by exactly one pure deterministic stage. A null result
    /// fences the caller. Providers never advance a stage unless its output fingerprint is durable.
    /// </summary>
    Task<GraphProjectionClaim?> AdvanceClaimStageAsync(
        GraphProjectionClaim claim,
        GraphProjectionStageTransition transition,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically validates and publishes one ComponentValidated replacement, advances the applied
    /// watermark, and records ComponentPublished under its current claim and fencing epoch.
    /// </summary>
    Task<bool> CommitClaimAsync(
        GraphProjectionClaim claim,
        GraphComponentProjection projection,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Durably records one classified failed, waiting, or cancelled attempt.</summary>
    Task RecordClaimFailureAsync(
        GraphProjectionClaim claim,
        GraphProjectionFailure failure,
        GraphJobExecutionState state,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Requests or acknowledges one durable run-control transition.</summary>
    Task<GraphOperationResult> SetRunControlAsync(
        GraphRunControlRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically performs bounded stale-row cleanup and marks the fully drained run Search-visible.</summary>
    Task<GraphOperationResult> CompleteProjectionAsync(
        GraphProjectionRun run,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns eligible failures to pending state without resetting successful work.</summary>
    Task<GraphOperationResult> RetryFailedAsync(
        string? workId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one privacy-bounded node page.</summary>
    Task<GraphPage<GraphNode>> GetNodesAsync(GraphNodeQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns one privacy-bounded node detail.</summary>
    Task<GraphNodeDetails?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>Returns one bounded evidence-backed fact page for a node.</summary>
    Task<GraphPage<GraphFact>> GetFactsAsync(GraphFactQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns one bounded timestamp-fact timeline page for a node.</summary>
    Task<GraphPage<GraphTimelineEntry>> GetTimelineAsync(GraphTimelineQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns one edge for semantic manual-decision validation.</summary>
    Task<GraphEdge?> GetEdgeAsync(string edgeId, CancellationToken cancellationToken = default);

    /// <summary>Returns one inactive or confirmed mention for semantic decision validation.</summary>
    Task<GraphMention?> GetMentionAsync(string mentionId, CancellationToken cancellationToken = default);

    /// <summary>Returns one bounded direct or explicitly experimental neighbor page.</summary>
    Task<GraphPage<GraphNeighbor>> GetNeighborsAsync(GraphNeighborQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns evidence retained by one edge.</summary>
    Task<IReadOnlyList<GraphEvidenceReference>> GetEvidenceAsync(
        string edgeId,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded one-hop Search expansions for existing file IDs.</summary>
    Task<IReadOnlyList<GraphSearchExpansion>> GetSearchExpansionsAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns independent graph-projection coverage.</summary>
    Task<GraphProjectionCoverage> GetCoverageAsync(CancellationToken cancellationToken = default);

    /// <summary>Inspects graph-owned data for one scope without reading source files.</summary>
    Task<GraphPrivacyInspection> InspectPrivacyAsync(
        GraphPrivacyScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one transactional graph-only privacy action.</summary>
    Task<GraphOperationResult> ApplyPrivacyAsync(
        GraphPrivacyChange change,
        GraphDecisionSnapshot decisionSnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Runs one bounded verification or selective repair operation.</summary>
    Task<GraphOperationResult> RepairAsync(
        GraphRepairRequest request,
        GraphDecisionSnapshot decisionSnapshot,
        GraphAuthoritySnapshot authoritySnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Invalidates only components affected by a graph-native decision.</summary>
    Task InvalidateDecisionAsync(
        GraphDecisionEntry decision,
        GraphDecisionSnapshot decisionSnapshot,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns aggregate diagnostics that omit query text and source content.</summary>
    Task<GraphDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persists the authoritative append-only graph-native decision namespace.</summary>
public interface IGraphDecisionStore : IAsyncDisposable
{
    /// <summary>Initializes and validates the decision ledger and recovery checkpoint.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the current validated decision checkpoint.</summary>
    Task<GraphDecisionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns authoritative opt-in and graph resource settings; a new store is disabled.</summary>
    Task<GraphControlSettings> GetControlSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists opt-in or resource settings using optimistic revision control.</summary>
    Task<GraphControlSettings> SetControlSettingsAsync(
        GraphControlSettings settings,
        long expectedRevision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the complete schema-3 decision manifest currently mirrored for compatibility.</summary>
    Task<string?> GetLegacyMirrorManifestIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Begins or resumes a bounded non-authoritative schema-3 decision-mirror generation.</summary>
    Task BeginLegacyMirrorAsync(
        string manifestId,
        long expectedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Stages one ordered page from the immutable completed schema-3 decision manifest.</summary>
    Task StageLegacyMirrorPageAsync(
        string manifestId,
        long pageSequence,
        IReadOnlyList<GraphLegacyDecisionObservation> observations,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically publishes a fully staged mirror and tombstones rows absent from the completed manifest.</summary>
    Task CompleteLegacyMirrorAsync(
        string manifestId,
        long expectedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Lists bounded managed recovery points without exposing storage paths or decision contents.</summary>
    Task<IReadOnlyList<GraphDecisionRecoveryPoint>> GetRecoveryPointsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Restores one verified managed recovery point after an explicit privacy-safe confirmation.</summary>
    Task<GraphOperationResult> RestoreAsync(
        string recoveryPointId,
        string confirmationText,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Appends one validated command transactionally at its expected sequence.</summary>
    Task<GraphDecisionEntry> AppendAsync(
        GraphDecisionCommand command,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded ordered ledger page for deterministic replay.</summary>
    Task<IReadOnlyList<GraphDecisionEntry>> ReadAsync(
        long afterSequence,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Irreversibly clears graph-native decisions only after explicit provider validation and backup handling.</summary>
    Task<GraphOperationResult> ClearAsync(
        string confirmationText,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Coordinates durable graph initialization, recovery, projection, and user control.</summary>
public interface IGraphProjectionCoordinator : IAsyncDisposable
{
    /// <summary>Raised after a meaningful durable lifecycle transition.</summary>
    event EventHandler<GraphCoordinatorStatus>? StatusChanged;

    /// <summary>Initializes stores and recovers interrupted claims.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns current durable lifecycle state.</summary>
    Task<GraphCoordinatorStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns authoritative opt-in and graph resource settings.</summary>
    Task<GraphControlSettings> GetControlSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates resource policy without changing graph opt-in or consent.</summary>
    Task<GraphControlSettings> UpdateResourceSettingsAsync(
        GraphResourceControlUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>Runs reviewed graph-only maintenance at a safe resource and control boundary.</summary>
    Task<GraphMaintenanceResult> MaintainAsync(
        GraphMaintenanceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Enables graph projection after explicit local consent.</summary>
    Task<GraphOperationResult> EnableAsync(bool consentConfirmed, CancellationToken cancellationToken = default);

    /// <summary>Disables graph work and queries while retaining graph-owned data.</summary>
    Task<GraphOperationResult> DisableAsync(CancellationToken cancellationToken = default);

    /// <summary>Captures and processes one completed source manifest.</summary>
    Task<GraphOperationResult> ReconcileAsync(CancellationToken cancellationToken = default);

    /// <summary>Durably pauses new claims and acknowledges a safe boundary.</summary>
    Task<GraphOperationResult> PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Durably resumes a paused run.</summary>
    Task<GraphOperationResult> ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests cooperative durable cancellation.</summary>
    Task<GraphOperationResult> CancelAsync(string reasonCode, CancellationToken cancellationToken = default);

    /// <summary>Retries one or all eligible failures without repeating successful work.</summary>
    Task<GraphOperationResult> RetryAsync(string? workId = null, CancellationToken cancellationToken = default);

    /// <summary>Stops new claims and waits only for the bounded cooperative shutdown grace period.</summary>
    Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default);
}

/// <summary>Runs coalesced graph reconciliation automatically around indexing lifecycle signals.</summary>
public interface IGraphReconciliationSignal
{
    /// <summary>Requests one coalesced graph reconciliation.</summary>
    ValueTask SignalAsync(CancellationToken cancellationToken = default);
}

/// <summary>Runs coalesced graph reconciliation automatically around indexing lifecycle signals.</summary>
public interface IGraphBackgroundRuntime : IGraphReconciliationSignal, IAsyncDisposable
{
    /// <summary>Initializes recovery, subscribes to indexing progress, and resumes enabled graph work.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops subscriptions, requests bounded coordinator shutdown, and observes the worker task.</summary>
    Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default);
}

/// <summary>Provides privacy-barriered paged graph reads.</summary>
public interface IGraphQueryService
{
    /// <summary>Returns one bounded node page.</summary>
    Task<GraphPage<GraphNode>> GetNodesPageAsync(GraphNodeQuery query, CancellationToken cancellationToken = default);
    /// <summary>Returns one node inspector projection.</summary>
    Task<GraphNodeDetails?> GetNodeDetailAsync(string nodeId, CancellationToken cancellationToken = default);
    /// <summary>Returns bounded evidence-backed facts for one node.</summary>
    Task<GraphPage<GraphFact>> GetFactsPageAsync(GraphFactQuery query, CancellationToken cancellationToken = default);
    /// <summary>Returns bounded timestamp facts as a node timeline.</summary>
    Task<GraphPage<GraphTimelineEntry>> GetTimelinePageAsync(GraphTimelineQuery query, CancellationToken cancellationToken = default);
    /// <summary>Returns bounded neighbors with the exact connecting edge and evidence.</summary>
    Task<GraphPage<GraphNeighbor>> GetNeighborsPageAsync(GraphNeighborQuery query, CancellationToken cancellationToken = default);
    /// <summary>Returns the retained evidence used by one edge.</summary>
    Task<IReadOnlyList<GraphEvidenceReference>> GetEvidenceAsync(string edgeId, CancellationToken cancellationToken = default);
    /// <summary>Returns graph coverage independently from Search-index coverage.</summary>
    Task<GraphProjectionCoverage> GetCoverageAsync(CancellationToken cancellationToken = default);
}

/// <summary>Supplies optional provider-neutral graph Search expansion.</summary>
public interface IGraphSearchSource
{
    /// <summary>Returns bounded expansions plus graph projection availability and coverage.</summary>
    Task<GraphSearchResult> ExpandAsync(GraphSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Inspects and forgets graph-owned data without affecting original files.</summary>
public interface IGraphPrivacyService
{
    /// <summary>Inspects graph-owned records for one scope.</summary>
    Task<GraphPrivacyInspection> InspectAsync(GraphPrivacyScope scope, CancellationToken cancellationToken = default);
    /// <summary>Applies one explicit graph-only privacy action.</summary>
    Task<GraphOperationResult> ApplyAsync(GraphPrivacyChange change, CancellationToken cancellationToken = default);
}

/// <summary>Exposes verified graph-decision recovery without leaking provider paths into UI or maintainers.</summary>
public interface IGraphDecisionRecoveryService
{
    /// <summary>Lists bounded privacy-safe managed decision recovery points.</summary>
    Task<IReadOnlyList<GraphDecisionRecoveryPoint>> GetRecoveryPointsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Restores one verified recovery point after exact confirmation.</summary>
    Task<GraphOperationResult> RestoreAsync(
        string recoveryPointId,
        string confirmationText,
        CancellationToken cancellationToken = default);
}

/// <summary>Exposes reviewed recovery of only the rebuildable derived graph sidecar.</summary>
public interface IGraphDerivedStoreRecoveryService
{
    /// <summary>Replaces a corrupt derived store while preserving authoritative decisions and source data.</summary>
    Task<GraphOperationResult> RecoverAsync(
        string confirmationText,
        CancellationToken cancellationToken = default);
}

/// <summary>Verifies and selectively repairs graph-owned data.</summary>
public interface IGraphRepairService
{
    /// <summary>Runs one bounded, cancellable repair without rebuilding the deep index.</summary>
    Task<GraphOperationResult> ExecuteAsync(GraphRepairRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Provides aggregate privacy-safe graph diagnostics.</summary>
public interface IGraphDiagnosticsService
{
    /// <summary>Returns graph counts and failure categories without source content or absolute paths.</summary>
    Task<GraphDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>Validates and persists graph-native manual control.</summary>
public interface IGraphDecisionService
{
    /// <summary>Applies one validated graph-native decision.</summary>
    Task<GraphOperationResult> ApplyAsync(GraphDecisionCommand command, CancellationToken cancellationToken = default);
    /// <summary>Creates one stable manual entity.</summary>
    Task<GraphOperationResult> CreateManualEntityAsync(string entityId, string label, CancellationToken cancellationToken = default);
    /// <summary>Renames one manual entity.</summary>
    Task<GraphOperationResult> RenameManualEntityAsync(string entityId, string label, CancellationToken cancellationToken = default);
    /// <summary>Adds one bounded alias.</summary>
    Task<GraphOperationResult> AddAliasAsync(string entityId, string alias, CancellationToken cancellationToken = default);
    /// <summary>Removes one exact alias from a manual entity.</summary>
    Task<GraphOperationResult> RemoveAliasAsync(string entityId, string alias, CancellationToken cancellationToken = default);
    /// <summary>Links two nodes with a manual edge.</summary>
    Task<GraphOperationResult> LinkAsync(string sourceNodeId, string targetNodeId, string reason, CancellationToken cancellationToken = default);
    /// <summary>Unlinks two nodes or rejects an automatic edge.</summary>
    Task<GraphOperationResult> UnlinkAsync(string edgeId, bool preventRegeneration, CancellationToken cancellationToken = default);
    /// <summary>Merges compatible manual or confirmed experimental entities.</summary>
    Task<GraphOperationResult> MergeAsync(string targetEntityId, string sourceEntityId, CancellationToken cancellationToken = default);
    /// <summary>Splits compatible manual or confirmed experimental entities.</summary>
    Task<GraphOperationResult> SplitAsync(string entityId, string memberId, CancellationToken cancellationToken = default);
    /// <summary>Rejects one inactive suggestion.</summary>
    Task<GraphOperationResult> RejectSuggestionAsync(string suggestionId, CancellationToken cancellationToken = default);
}

/// <summary>Strictly validates optional untrusted entity suggestions.</summary>
public interface IGraphSuggestionValidator
{
    /// <summary>Returns only bounded confirmation-required suggestions; disabled options return no suggestions.</summary>
    IReadOnlyList<GraphValidatedSuggestion> Validate(
        IReadOnlyList<GraphSuggestionCandidate> candidates,
        GraphSuggestionOptions? options = null,
        CancellationToken cancellationToken = default);
}
