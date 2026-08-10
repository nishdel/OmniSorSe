namespace OpenSorSe.Application.KnowledgeGraph;

internal sealed record GraphReadAuthorityFence(
    long DecisionSequence,
    string DecisionCheckpointId,
    long PrivacySequence,
    string LegacyDecisionManifestId,
    string? SourceManifestId,
    long SourceRevision,
    long AppliedRevision,
    GraphControlSettings ControlSettings);

/// <summary>Thrown when current privacy or decision authority cannot permit a graph operation.</summary>
public sealed class GraphAccessUnavailableException : InvalidOperationException
{
    /// <summary>Initializes an authority failure with a bounded reason.</summary>
    /// <param name="reasonCode">Privacy-safe reason code.</param>
    public GraphAccessUnavailableException(string reasonCode)
        : base(string.Concat("Graph access is unavailable: ", reasonCode)) => ReasonCode = reasonCode;

    /// <summary>Gets the privacy-safe failure category.</summary>
    public string ReasonCode { get; }
}

/// <summary>Provides bounded graph reads behind current privacy and decision authority.</summary>
public sealed class GraphQueryService : IGraphQueryService
{
    private readonly IGraphStore _store;
    private readonly IGraphProjectionSource _source;
    private readonly IGraphDecisionStore _decisionStore;
    private readonly IGraphStorageLifecycle _storageLifecycle;

    /// <summary>Initializes the graph query service.</summary>
    public GraphQueryService(
        IGraphStore store,
        IGraphProjectionSource source,
        IGraphDecisionStore decisionStore,
        IGraphStorageLifecycle? storageLifecycle = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _decisionStore = decisionStore ?? throw new ArgumentNullException(nameof(decisionStore));
        _storageLifecycle = storageLifecycle ?? new AlwaysProvisionedGraphStorageLifecycle();
    }

    /// <inheritdoc />
    public async Task<GraphPage<GraphNode>> GetNodesPageAsync(
        GraphNodeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidatePageSize(query.PageSize);
        if (query.NormalizedLabelPrefix is { } prefix)
        {
            ValidateBounded(prefix, GraphLimits.MaximumLabelCharacters, allowEmpty: false);
        }

        var fence = await EnsureAuthorityAsync([], "query-nodes", null, cancellationToken).ConfigureAwait(false);
        var page = await _store.GetNodesAsync(query, cancellationToken).ConfigureAwait(false);
        await EnsureAuthorityAsync([], "query-nodes", fence, cancellationToken).ConfigureAwait(false);
        ValidateNodePage(page, query.PageSize);
        return page;
    }

    /// <inheritdoc />
    public async Task<GraphNodeDetails?> GetNodeDetailAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateId(nodeId);
        var fence = await EnsureAuthorityAsync([nodeId], "query-node", null, cancellationToken).ConfigureAwait(false);
        var details = await _store.GetNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
        await EnsureAuthorityAsync([nodeId], "query-node", fence, cancellationToken).ConfigureAwait(false);
        if (details is not null && (!GraphBoundaryValidator.IsValid(details.Node) ||
            details.Aliases is null || details.Aliases.Count > GraphLimits.MaximumAliasesPerNode ||
            details.Aliases.Distinct(StringComparer.Ordinal).Count() != details.Aliases.Count ||
            details.Aliases.Any(alias => !IsBounded(alias, GraphLimits.MaximumLabelCharacters)) ||
            details.IncomingEdgeCount < 0 || details.OutgoingEdgeCount < 0))
        {
            throw new GraphAccessUnavailableException("graph-record-invalid");
        }

        return details;
    }

    /// <inheritdoc />
    public async Task<GraphPage<GraphFact>> GetFactsPageAsync(
        GraphFactQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateId(query.NodeId);
        ValidatePageSize(query.PageSize);
        if (!GraphBoundaryValidator.IsValid(query.Cursor) || query.Kind is { } kind && !kind.IsStable)
        {
            throw new ArgumentException("The graph fact query is outside stable bounds.", nameof(query));
        }

        var fence = await EnsureAuthorityAsync([query.NodeId], "query-facts", null, cancellationToken).ConfigureAwait(false);
        var page = await _store.GetFactsAsync(query, cancellationToken).ConfigureAwait(false);
        await EnsureAuthorityAsync([query.NodeId], "query-facts", fence, cancellationToken).ConfigureAwait(false);
        if (page is null || page.Items is null || page.Items.Count > query.PageSize ||
            !GraphBoundaryValidator.IsValid(page.NextCursor) || page.TotalCount is < 0 ||
            page.Items.Any(item => !GraphBoundaryValidator.IsValid(item) ||
                                   !string.Equals(item.SubjectNodeId, query.NodeId, StringComparison.Ordinal)) ||
            page.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != page.Items.Count)
        {
            throw new GraphAccessUnavailableException("graph-record-invalid");
        }

        return page;
    }

    /// <inheritdoc />
    public async Task<GraphPage<GraphTimelineEntry>> GetTimelinePageAsync(
        GraphTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateId(query.NodeId);
        ValidatePageSize(query.PageSize);
        if (!GraphBoundaryValidator.IsValid(query.Cursor) ||
            query.FromUtc is { } from && query.ToUtc is { } to && from > to)
        {
            throw new ArgumentException("The graph timeline query is outside stable bounds.", nameof(query));
        }

        var fence = await EnsureAuthorityAsync([query.NodeId], "query-timeline", null, cancellationToken).ConfigureAwait(false);
        var page = await _store.GetTimelineAsync(query, cancellationToken).ConfigureAwait(false);
        await EnsureAuthorityAsync([query.NodeId], "query-timeline", fence, cancellationToken).ConfigureAwait(false);
        if (page is null || page.Items is null || page.Items.Count > query.PageSize ||
            !GraphBoundaryValidator.IsValid(page.NextCursor) || page.TotalCount is < 0 ||
            page.Items.Any(item => !GraphBoundaryValidator.IsValid(item) ||
                                   !string.Equals(item.SubjectNodeId, query.NodeId, StringComparison.Ordinal)) ||
            page.Items.Select(item => item.FactId).Distinct(StringComparer.Ordinal).Count() != page.Items.Count)
        {
            throw new GraphAccessUnavailableException("graph-record-invalid");
        }

        return page;
    }

    /// <inheritdoc />
    public async Task<GraphPage<GraphNeighbor>> GetNeighborsPageAsync(
        GraphNeighborQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateId(query.NodeId);
        ValidatePageSize(query.PageSize);
        var maximumDepth = query.ExperimentalTraversal
            ? GraphLimits.MaximumExperimentalTraversalDepth
            : GraphLimits.StableTraversalDepth;
        if (query.Depth is < 1 || query.Depth > maximumDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Graph traversal depth exceeds the selected stable or experimental ceiling.");
        }

        var fence = await EnsureAuthorityAsync([query.NodeId], "query-neighbors", null, cancellationToken).ConfigureAwait(false);
        var page = await _store.GetNeighborsAsync(query, cancellationToken).ConfigureAwait(false);
        await EnsureAuthorityAsync([query.NodeId], "query-neighbors", fence, cancellationToken).ConfigureAwait(false);
        if (page.Items is null || page.Items.Count > query.PageSize || page.TotalCount is < 0 ||
            !GraphBoundaryValidator.IsValid(page.NextCursor) || page.Items.Any(item =>
                item is null || !GraphBoundaryValidator.IsValid(item.Node) || !GraphBoundaryValidator.IsValid(item.Edge) ||
                item.Evidence is null || item.Evidence.Count > GraphLimits.MaximumEvidencePerEdge ||
                item.Evidence.Any(evidence => !GraphBoundaryValidator.IsValid(evidence)) ||
                !item.Edge.EvidenceIds.All(id => item.Evidence.Any(evidence => evidence.Id == id)) ||
                !(item.Edge.SourceNodeId == query.NodeId && item.Edge.TargetNodeId == item.Node.Identity.NodeId) &&
                !(item.Edge.TargetNodeId == query.NodeId && item.Edge.SourceNodeId == item.Node.Identity.NodeId)))
        {
            throw new GraphAccessUnavailableException("graph-record-invalid");
        }

        return page;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphEvidenceReference>> GetEvidenceAsync(
        string edgeId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(edgeId);
        var fence = await EnsureAuthorityAsync([edgeId], "query-evidence", null, cancellationToken).ConfigureAwait(false);
        var evidence = await _store
            .GetEvidenceAsync(edgeId, GraphLimits.MaximumEvidencePerEdge, cancellationToken)
            .ConfigureAwait(false);
        await EnsureAuthorityAsync([edgeId], "query-evidence", fence, cancellationToken).ConfigureAwait(false);
        if (evidence is null || evidence.Count > GraphLimits.MaximumEvidencePerEdge ||
            evidence.Any(item => !GraphBoundaryValidator.IsValid(item)) ||
            evidence.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != evidence.Count)
        {
            throw new GraphAccessUnavailableException("graph-record-invalid");
        }

        return evidence;
    }

    /// <inheritdoc />
    public async Task<GraphProjectionCoverage> GetCoverageAsync(CancellationToken cancellationToken = default)
    {
        await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
        var coverage = await _store.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        return GraphBoundaryValidator.IsValid(coverage)
            ? coverage
            : throw new GraphAccessUnavailableException("graph-store-invalid");
    }

    private static void ValidateNodePage(GraphPage<GraphNode> page, int requestedPageSize)
    {
        if (page is null || page.Items is null || page.Items.Count > requestedPageSize || page.TotalCount is < 0 ||
            !GraphBoundaryValidator.IsValid(page.NextCursor) ||
            page.Items.Any(item => !GraphBoundaryValidator.IsValid(item)) ||
            page.Items.Select(item => item.Identity.NodeId).Distinct(StringComparer.Ordinal).Count() != page.Items.Count)
        {
            throw new GraphAccessUnavailableException("graph-record-invalid");
        }
    }

    private static bool IsBounded(string value, int maximum) =>
        value is not null && value.Length <= maximum && !string.IsNullOrWhiteSpace(value) &&
        !ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(value);

    private async Task<GraphReadAuthorityFence> EnsureAuthorityAsync(
        IReadOnlyList<string> stableKeys,
        string operation,
        GraphReadAuthorityFence? expected,
        CancellationToken cancellationToken)
    {
        await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
        GraphDecisionSnapshot decisionSnapshot;
        GraphControlSettings controlSettings;
        try
        {
            decisionSnapshot = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            controlSettings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new GraphAccessUnavailableException("decision-store-unavailable");
        }

        if (!GraphBoundaryValidator.IsValid(decisionSnapshot))
        {
            throw new GraphAccessUnavailableException("decision-store-invalid");
        }
        GraphResourceAdmissionPolicy.Validate(controlSettings);
        if (!controlSettings.IsEnabled)
        {
            throw new GraphAccessUnavailableException("graph-disabled");
        }

        var authority = await _source
            .ValidateAuthorityAsync(new GraphAuthorityRequest(stableKeys, operation), cancellationToken)
            .ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(authority) || !authority.IsAvailable || !authority.IsAllowed)
        {
            throw new GraphAccessUnavailableException(authority.ReasonCode);
        }

        var coverage = await _store.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(coverage))
        {
            throw new GraphAccessUnavailableException("graph-store-invalid");
        }

        if (!coverage.IsEnabled || !coverage.IsAvailable || coverage.IsStale ||
            !string.Equals(coverage.AppliedManifestId, authority.CurrentSourceManifestId, StringComparison.Ordinal) ||
            coverage.AppliedRevision != authority.CurrentSourceRevision ||
            coverage.AppliedDecisionSequence != decisionSnapshot.Sequence ||
            !string.Equals(coverage.AppliedDecisionCheckpointId, decisionSnapshot.CheckpointId, StringComparison.Ordinal) ||
            coverage.AppliedPrivacySequence < authority.PrivacySequence)
        {
            throw new GraphAccessUnavailableException(
                !coverage.IsEnabled ? "graph-disabled" :
                !coverage.IsAvailable ? "graph-unavailable" :
                coverage.IsStale ? "graph-stale" :
                !string.Equals(coverage.AppliedManifestId, authority.CurrentSourceManifestId, StringComparison.Ordinal) ||
                coverage.AppliedRevision != authority.CurrentSourceRevision
                    ? "source-watermark-pending"
                    : "authority-watermark-pending");
        }

        var current = new GraphReadAuthorityFence(
            decisionSnapshot.Sequence,
            decisionSnapshot.CheckpointId,
            authority.PrivacySequence,
            authority.LegacyDecisionManifestId,
            authority.CurrentSourceManifestId,
            authority.CurrentSourceRevision,
            coverage.AppliedRevision,
            controlSettings);
        if (expected is not null && current != expected)
        {
            throw new GraphAccessUnavailableException("authority-changed-during-read");
        }

        return current;
    }

    internal static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > GraphLimits.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
    }

    internal static void ValidateId(string value) => ValidateBounded(value, GraphLimits.MaximumStableIdCharacters, allowEmpty: false);

    internal static void ValidateBounded(string value, int maximumLength, bool allowEmpty)
    {
        if (value is null || value.Length > maximumLength ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(value) ||
            (!allowEmpty && string.IsNullOrWhiteSpace(value)))
        {
            throw new ArgumentException("Graph input is empty, malformed, or exceeds its safety limit.", nameof(value));
        }
    }
}

/// <summary>Supplies failure-isolated one-hop graph expansion to Search.</summary>
public sealed class GraphSearchSource : IGraphSearchSource
{
    private readonly IGraphStore _store;
    private readonly IGraphProjectionSource _source;
    private readonly IGraphDecisionStore _decisionStore;
    private readonly IGraphStorageLifecycle _storageLifecycle;

    /// <summary>Initializes the graph Search source.</summary>
    public GraphSearchSource(
        IGraphStore store,
        IGraphProjectionSource source,
        IGraphDecisionStore decisionStore,
        IGraphStorageLifecycle? storageLifecycle = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _decisionStore = decisionStore ?? throw new ArgumentNullException(nameof(decisionStore));
        _storageLifecycle = storageLifecycle ?? new AlwaysProvisionedGraphStorageLifecycle();
    }

    /// <inheritdoc />
    public async Task<GraphSearchResult> ExpandAsync(GraphSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumExpansions is < 1 or > GraphLimits.MaximumGraphSearchExpansions)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var seeds = request.SeedFileIds
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Take(GraphLimits.MaximumSearchSeeds + 1)
            .ToArray();
        if (seeds.Length > GraphLimits.MaximumSearchSeeds || seeds.Length != request.SeedFileIds.Count)
        {
            throw new ArgumentException("Graph Search seeds must be unique, non-empty, and within the shared seed ceiling.", nameof(request));
        }

        foreach (var seed in seeds)
        {
            GraphQueryService.ValidateId(seed);
        }

        GraphProjectionCoverage coverage;
        try
        {
            await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
            var controlSettings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            GraphResourceAdmissionPolicy.Validate(controlSettings);
            if (!controlSettings.IsEnabled)
            {
                return Unavailable(await TryCoverageAsync(cancellationToken).ConfigureAwait(false), "graph-disabled");
            }

            coverage = await _store.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
            var decisionSnapshot = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(decisionSnapshot))
            {
                return Unavailable(coverage, "decision-store-invalid");
            }

            var authority = await _source
                .ValidateAuthorityAsync(new GraphAuthorityRequest(seeds, "search-expand"), cancellationToken)
                .ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(coverage) || !GraphBoundaryValidator.IsValid(authority) ||
                !coverage.IsEnabled || !coverage.IsAvailable || coverage.IsStale ||
                !authority.IsAvailable || !authority.IsAllowed ||
                !string.Equals(coverage.AppliedManifestId, authority.CurrentSourceManifestId, StringComparison.Ordinal) ||
                coverage.AppliedRevision != authority.CurrentSourceRevision ||
                coverage.AppliedDecisionSequence != decisionSnapshot.Sequence ||
                !string.Equals(coverage.AppliedDecisionCheckpointId, decisionSnapshot.CheckpointId, StringComparison.Ordinal) ||
                coverage.AppliedPrivacySequence < authority.PrivacySequence)
            {
                return Unavailable(coverage, authority.ReasonCode);
            }

            var fence = new GraphReadAuthorityFence(
                decisionSnapshot.Sequence,
                decisionSnapshot.CheckpointId,
                authority.PrivacySequence,
                authority.LegacyDecisionManifestId,
                authority.CurrentSourceManifestId,
                authority.CurrentSourceRevision,
                coverage.AppliedRevision,
                controlSettings);

            var expansion = await _store
                .GetSearchExpansionsAsync(seeds, request.MaximumExpansions, cancellationToken)
                .ConfigureAwait(false);
            if (expansion is null || expansion.Count > request.MaximumExpansions || expansion.Any(item => !IsSafeExpansion(item)))
            {
                throw new InvalidDataException("The graph Search provider returned a malformed expansion set.");
            }

            var seedSet = seeds.ToHashSet(StringComparer.Ordinal);
            var bounded = expansion
                .Where(item => seedSet.Contains(item.SeedFileId) && item.SeedFileId != item.RelatedFileId)
                .Where(IsSafeExpansion)
                .GroupBy(item => item.RelatedFileId, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(item => item.Confidence)
                    .ThenBy(item => item.EdgeId, StringComparer.Ordinal)
                    .First())
                .OrderByDescending(item => item.Confidence)
                .ThenBy(item => item.RelatedFileId, StringComparer.Ordinal)
                .Take(request.MaximumExpansions)
                .ToArray();
            var finalDecisionSnapshot = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var finalControlSettings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
            var finalAuthority = await _source
                .ValidateAuthorityAsync(new GraphAuthorityRequest(seeds, "search-expand"), cancellationToken)
                .ConfigureAwait(false);
            var finalCoverage = await _store.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
            var finalFence = new GraphReadAuthorityFence(
                finalDecisionSnapshot.Sequence,
                finalDecisionSnapshot.CheckpointId,
                finalAuthority.PrivacySequence,
                finalAuthority.LegacyDecisionManifestId,
                finalAuthority.CurrentSourceManifestId,
                finalAuthority.CurrentSourceRevision,
                finalCoverage.AppliedRevision,
                finalControlSettings);
            if (!GraphBoundaryValidator.IsValid(finalDecisionSnapshot) ||
                !GraphBoundaryValidator.IsValid(finalAuthority) || !GraphBoundaryValidator.IsValid(finalCoverage) ||
                !finalControlSettings.IsEnabled ||
                !finalAuthority.IsAvailable || !finalAuthority.IsAllowed ||
                !finalCoverage.IsEnabled || !finalCoverage.IsAvailable || finalCoverage.IsStale ||
                finalCoverage.AppliedDecisionSequence != finalDecisionSnapshot.Sequence ||
                !string.Equals(finalCoverage.AppliedManifestId, finalAuthority.CurrentSourceManifestId, StringComparison.Ordinal) ||
                finalCoverage.AppliedRevision != finalAuthority.CurrentSourceRevision ||
                !string.Equals(finalCoverage.AppliedDecisionCheckpointId, finalDecisionSnapshot.CheckpointId, StringComparison.Ordinal) ||
                finalCoverage.AppliedPrivacySequence < finalAuthority.PrivacySequence || finalFence != fence)
            {
                return Unavailable(finalCoverage, "authority-changed-during-read");
            }

            return new GraphSearchResult(bounded, finalCoverage, true, "Graph context is available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GraphAccessUnavailableException exception)
        {
            coverage = await TryCoverageAsync(cancellationToken).ConfigureAwait(false);
            return Unavailable(coverage, exception.ReasonCode);
        }
        catch (Exception exception)
        {
            coverage = await TryCoverageAsync(cancellationToken).ConfigureAwait(false);
            return Unavailable(coverage, exception.GetType().Name);
        }
    }

    private async Task<GraphProjectionCoverage> TryCoverageAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
            return await _store.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new GraphProjectionCoverage(false, false, false, false, 0, 0, 0, 0, null, 0, "Graph coverage is unavailable.");
        }
    }

    private static bool IsSafeExpansion(GraphSearchExpansion expansion) =>
        expansion is not null &&
        IsSafeText(expansion.SeedFileId, GraphLimits.MaximumStableIdCharacters) &&
        IsSafeText(expansion.RelatedFileId, GraphLimits.MaximumStableIdCharacters) &&
        IsSafeText(expansion.EdgeId, GraphLimits.MaximumStableIdCharacters) &&
        expansion.EdgeKind.IsStable && Enum.IsDefined(expansion.Confidence) &&
        Enum.IsDefined(expansion.Freshness) && expansion.ProjectionRevision >= 0 &&
        IsSafeText(expansion.Explanation, GraphLimits.MaximumEvidenceTextCharacters);

    private static bool IsSafeText(string? value, int maximumLength) =>
        value is not null && value.Length <= maximumLength && !string.IsNullOrWhiteSpace(value) &&
        !ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(value);

    private static GraphSearchResult Unavailable(GraphProjectionCoverage coverage, string reason)
    {
        var safeCoverage = GraphBoundaryValidator.IsValid(coverage)
            ? coverage with { IsAvailable = false }
            : new GraphProjectionCoverage(
                false,
                false,
                false,
                true,
                0,
                0,
                0,
                0,
                null,
                0,
                "Graph coverage is unavailable.");
        var safeReason = IsSafeText(reason, 128) ? reason : "provider-record-invalid";
        return new GraphSearchResult(
            [],
            safeCoverage,
            false,
            string.Concat("Graph context is unavailable: ", safeReason));
    }
}

/// <summary>Provides graph-only privacy inspection and transactional forgetting.</summary>
public sealed class GraphPrivacyService : IGraphPrivacyService
{
    private const string ClearDecisionsConfirmation = "CLEAR GRAPH DECISIONS";
    private readonly IGraphStore _store;
    private readonly IGraphDecisionStore _decisionStore;
    private readonly IGraphProjectionSource _source;
    private readonly IGraphReconciliationSignal _reconciliationSignal;
    private readonly TimeProvider _timeProvider;
    private readonly IGraphStorageLifecycle _storageLifecycle;

    /// <summary>Initializes the graph privacy service.</summary>
    public GraphPrivacyService(
        IGraphStore store,
        IGraphDecisionStore decisionStore,
        IGraphProjectionSource source,
        IGraphReconciliationSignal reconciliationSignal,
        TimeProvider? timeProvider = null,
        IGraphStorageLifecycle? storageLifecycle = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _decisionStore = decisionStore ?? throw new ArgumentNullException(nameof(decisionStore));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _reconciliationSignal = reconciliationSignal ?? throw new ArgumentNullException(nameof(reconciliationSignal));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _storageLifecycle = storageLifecycle ?? new AlwaysProvisionedGraphStorageLifecycle();
    }

    /// <inheritdoc />
    public async Task<GraphPrivacyInspection> InspectAsync(GraphPrivacyScope scope, CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        var fence = await EnsureAuthorityAsync(scope, "privacy-inspect", requireAppliedSource: true, requireEnabled: true, null, cancellationToken).ConfigureAwait(false);
        var inspection = await _store.InspectPrivacyAsync(scope, cancellationToken).ConfigureAwait(false);
        await EnsureAuthorityAsync(scope, "privacy-inspect", requireAppliedSource: true, requireEnabled: true, fence, cancellationToken).ConfigureAwait(false);
        if (inspection is null || inspection.Scope != scope || inspection.NodeCount < 0 || inspection.EdgeCount < 0 ||
            inspection.EvidenceCount < 0 || inspection.AliasCount < 0 || inspection.DecisionCount < 0 ||
            !IsSafeInspectionMessage(inspection.Message))
        {
            throw new GraphAccessUnavailableException("graph-record-invalid");
        }

        return inspection;
    }

    private static bool IsSafeInspectionMessage(string? message) =>
        message is not null && message.Length <= GraphLimits.MaximumDecisionReasonCharacters &&
        !ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(message);

    /// <inheritdoc />
    public async Task<GraphOperationResult> ApplyAsync(GraphPrivacyChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ValidateScope(change.Scope);
        if (!change.ConfirmSourceFilesUnaffected)
        {
            throw new InvalidOperationException("Graph privacy actions require confirmation that original files are unaffected.");
        }

        await EnsureAuthorityAsync(change.Scope, "privacy-change", requireAppliedSource: false, requireEnabled: false, null, cancellationToken).ConfigureAwait(false);
        if (change.Action == GraphPrivacyAction.ClearAllDecisions &&
            !string.Equals(change.ConfirmationText, ClearDecisionsConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Clearing graph-native decisions requires the exact disclosed confirmation text.");
        }

        if (change.Action is GraphPrivacyAction.ClearAllDerivedData or GraphPrivacyAction.ClearAllDecisions)
        {
            await DisableForClearAsync(cancellationToken).ConfigureAwait(false);
        }

        GraphDecisionSnapshot appliedDecisionSnapshot;
        if (change.Action == GraphPrivacyAction.ClearAllDecisions)
        {
            var cleared = await _decisionStore
                .ClearAsync(change.ConfirmationText!, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            if (!cleared.Succeeded)
            {
                return cleared;
            }

            var clearedCheckpoint = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(clearedCheckpoint) || clearedCheckpoint.Sequence != 0)
            {
                throw new GraphAccessUnavailableException("decision-clear-not-recoverable");
            }
            appliedDecisionSnapshot = clearedCheckpoint;
        }
        else if (change.Action is GraphPrivacyAction.ForgetDerivedData or
                 GraphPrivacyAction.ExcludeFromProjection or
                 GraphPrivacyAction.IncludeInProjection)
        {
            var before = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(before))
            {
                throw new GraphAccessUnavailableException("decision-store-invalid");
            }

            var command = new GraphDecisionCommand
            {
                Kind = change.Action switch
                {
                    GraphPrivacyAction.ForgetDerivedData => GraphDecisionKind.Forget,
                    GraphPrivacyAction.ExcludeFromProjection => GraphDecisionKind.Exclude,
                    _ => GraphDecisionKind.Include,
                },
                SubjectId = change.Scope.Kind == GraphPrivacyScopeKind.All ? "graph:all" : change.Scope.StableId,
                Reason = string.Concat("privacy-scope-", change.Scope.Kind.ToString().ToLowerInvariant()),
                ExpectedSequence = before.Sequence,
            };
            var entry = await _decisionStore
                .AppendAsync(command, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            var recoveryPoint = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(recoveryPoint) || recoveryPoint.Sequence < entry.Sequence)
            {
                throw new GraphAccessUnavailableException("privacy-floor-not-recoverable");
            }
            appliedDecisionSnapshot = recoveryPoint;
        }
        else
        {
            appliedDecisionSnapshot = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(appliedDecisionSnapshot))
            {
                throw new GraphAccessUnavailableException("decision-store-invalid");
            }
        }

        try
        {
            var result = await _store
                .ApplyPrivacyAsync(change, appliedDecisionSnapshot, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            await _reconciliationSignal.SignalAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (change.Action is GraphPrivacyAction.ForgetDerivedData or
                               GraphPrivacyAction.ExcludeFromProjection or
                               GraphPrivacyAction.IncludeInProjection)
        {
            await _reconciliationSignal.SignalAsync(cancellationToken).ConfigureAwait(false);
            return new GraphOperationResult(
                true,
                "The privacy decision is durable; derived graph reconciliation is pending.",
                1);
        }
    }

    private async Task DisableForClearAsync(CancellationToken cancellationToken)
    {
        var settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        GraphResourceAdmissionPolicy.Validate(settings);
        if (settings.IsEnabled)
        {
            settings = await _decisionStore.SetControlSettingsAsync(
                settings with { IsEnabled = false },
                settings.Revision,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }

        await _store.SetEnabledAsync(
            false,
            settings.ConsentConfirmed,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GraphReadAuthorityFence> EnsureAuthorityAsync(
        GraphPrivacyScope scope,
        string operation,
        bool requireAppliedSource,
        bool requireEnabled,
        GraphReadAuthorityFence? expected,
        CancellationToken cancellationToken)
    {
        await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
        GraphDecisionSnapshot decisionSnapshot;
        GraphControlSettings controlSettings;
        try
        {
            decisionSnapshot = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            controlSettings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new GraphAccessUnavailableException("decision-store-unavailable");
        }

        if (!GraphBoundaryValidator.IsValid(decisionSnapshot))
        {
            throw new GraphAccessUnavailableException("decision-store-invalid");
        }
        GraphResourceAdmissionPolicy.Validate(controlSettings);
        if (requireEnabled && !controlSettings.IsEnabled)
        {
            throw new GraphAccessUnavailableException("graph-disabled");
        }

        var keys = scope.Kind == GraphPrivacyScopeKind.All ? Array.Empty<string>() : [scope.StableId];
        var authority = await _source
            .ValidateAuthorityAsync(new GraphAuthorityRequest(keys, operation), cancellationToken)
            .ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(authority) || !authority.IsAvailable || !authority.IsAllowed)
        {
            throw new GraphAccessUnavailableException(authority.ReasonCode);
        }

        var coverage = await _store.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(coverage))
        {
            throw new GraphAccessUnavailableException("graph-store-invalid");
        }
        if ((requireAppliedSource &&
             (!string.Equals(coverage.AppliedManifestId, authority.CurrentSourceManifestId, StringComparison.Ordinal) ||
              coverage.AppliedRevision != authority.CurrentSourceRevision)) ||
            coverage.AppliedDecisionSequence != decisionSnapshot.Sequence ||
            !string.Equals(coverage.AppliedDecisionCheckpointId, decisionSnapshot.CheckpointId, StringComparison.Ordinal) ||
            coverage.AppliedPrivacySequence < authority.PrivacySequence)
        {
            throw new GraphAccessUnavailableException("authority-watermark-pending");
        }

        var current = new GraphReadAuthorityFence(
            decisionSnapshot.Sequence,
            decisionSnapshot.CheckpointId,
            authority.PrivacySequence,
            authority.LegacyDecisionManifestId,
            authority.CurrentSourceManifestId,
            authority.CurrentSourceRevision,
            coverage.AppliedRevision,
            controlSettings);
        if (expected is not null && current != expected)
        {
            throw new GraphAccessUnavailableException("authority-changed-during-read");
        }

        return current;
    }

    private static void ValidateScope(GraphPrivacyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Kind != GraphPrivacyScopeKind.All)
        {
            GraphQueryService.ValidateId(scope.StableId);
        }
    }
}

/// <summary>Coordinates bounded selective graph verification and repair.</summary>
public sealed class GraphRepairService : IGraphRepairService
{
    private readonly IGraphStore _store;
    private readonly IGraphProjectionSource _source;
    private readonly IGraphDecisionStore _decisionStore;
    private readonly IGraphReconciliationSignal _reconciliationSignal;
    private readonly TimeProvider _timeProvider;
    private readonly IGraphStorageLifecycle _storageLifecycle;

    /// <summary>Initializes the repair service.</summary>
    public GraphRepairService(
        IGraphStore store,
        IGraphProjectionSource source,
        IGraphDecisionStore decisionStore,
        IGraphReconciliationSignal reconciliationSignal,
        TimeProvider? timeProvider = null,
        IGraphStorageLifecycle? storageLifecycle = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _decisionStore = decisionStore ?? throw new ArgumentNullException(nameof(decisionStore));
        _reconciliationSignal = reconciliationSignal ?? throw new ArgumentNullException(nameof(reconciliationSignal));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _storageLifecycle = storageLifecycle ?? new AlwaysProvisionedGraphStorageLifecycle();
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> ExecuteAsync(GraphRepairRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
        var requiresId = request.Kind is GraphRepairKind.ReprojectComponent or GraphRepairKind.ReprojectFile or GraphRepairKind.ReprojectSource;
        if (requiresId)
        {
            GraphQueryService.ValidateId(request.StableId!);
        }

        if (request.Kind != GraphRepairKind.Verify && !request.ConfirmSourceFilesUnaffected)
        {
            throw new InvalidOperationException("Graph repair requires confirmation that original files remain unchanged.");
        }

        GraphDecisionSnapshot decisions;
        GraphAuthoritySnapshot authority;
        if (request.Kind == GraphRepairKind.Verify)
        {
            decisions = await TryDecisionSnapshotAsync(cancellationToken).ConfigureAwait(false);
            authority = await TryAuthoritySnapshotAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            decisions = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(decisions))
            {
                throw new GraphAccessUnavailableException("decision-store-invalid");
            }

            authority = await _source.ValidateAuthorityAsync(
                new GraphAuthorityRequest(request.StableId is null ? [] : [request.StableId], "graph-repair"),
                cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(authority) || !authority.IsAvailable || !authority.IsAllowed)
            {
                throw new GraphAccessUnavailableException(authority.ReasonCode);
            }
        }

        var result = await _store
            .RepairAsync(request, decisions, authority, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (request.Kind != GraphRepairKind.Verify)
        {
            var finalDecisions = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var finalAuthority = await _source.ValidateAuthorityAsync(
                new GraphAuthorityRequest(request.StableId is null ? [] : [request.StableId], "graph-repair"),
                cancellationToken).ConfigureAwait(false);
            if (!GraphBoundaryValidator.IsValid(finalDecisions) || finalDecisions.Sequence != decisions.Sequence ||
                !string.Equals(finalDecisions.CheckpointId, decisions.CheckpointId, StringComparison.Ordinal) ||
                !GraphBoundaryValidator.IsValid(finalAuthority) || !finalAuthority.IsAvailable || !finalAuthority.IsAllowed ||
                finalAuthority.PrivacySequence != authority.PrivacySequence ||
                !string.Equals(finalAuthority.LegacyDecisionManifestId, authority.LegacyDecisionManifestId, StringComparison.Ordinal) ||
                !string.Equals(finalAuthority.CurrentSourceManifestId, authority.CurrentSourceManifestId, StringComparison.Ordinal) ||
                finalAuthority.CurrentSourceRevision != authority.CurrentSourceRevision)
            {
                throw new GraphAccessUnavailableException("authority-changed-during-repair");
            }

            await _reconciliationSignal.SignalAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<GraphDecisionSnapshot> TryDecisionSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new GraphDecisionSnapshot(0, "unavailable", string.Empty, false);
        }
    }

    private async Task<GraphAuthoritySnapshot> TryAuthoritySnapshotAsync(
        GraphRepairRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _source.ValidateAuthorityAsync(
                new GraphAuthorityRequest(request.StableId is null ? [] : [request.StableId], "graph-verify"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new GraphAuthoritySnapshot(false, false, 0, string.Empty, "authority-unavailable");
        }
    }
}

/// <summary>Returns aggregate graph diagnostics without retaining private content.</summary>
public sealed class GraphDiagnosticsService : IGraphDiagnosticsService
{
    private readonly IGraphStore _store;
    private readonly IGraphStorageLifecycle? _storageLifecycle;

    /// <summary>Initializes the diagnostics facade.</summary>
    public GraphDiagnosticsService(IGraphStore store, IGraphStorageLifecycle? storageLifecycle = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _storageLifecycle = storageLifecycle;
    }

    /// <inheritdoc />
    public async Task<GraphDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_storageLifecycle is not null)
        {
            await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await _store.GetDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.SchemaVersion is < 1 or > 1024 ||
            !IsSafeDiagnosticCode(snapshot.ProviderCode, 64) || snapshot.AlgorithmVersions is null ||
            snapshot.AlgorithmVersions.Count > 32 ||
            snapshot.AlgorithmVersions.Distinct(StringComparer.Ordinal).Count() != snapshot.AlgorithmVersions.Count ||
            snapshot.AlgorithmVersions.Any(item => !IsSafeDiagnosticCode(item, 64)) ||
            snapshot.StageDurations is null || snapshot.StageDurations.Count > 32 ||
            snapshot.StageDurations.Select(item => item.StageCode).Distinct(StringComparer.Ordinal).Count() != snapshot.StageDurations.Count ||
            snapshot.StageDurations.Any(item => item is null || !IsSafeDiagnosticCode(item.StageCode, 64) ||
                item.InvocationCount < 0 || item.TotalDuration < TimeSpan.Zero || item.MaximumDuration < TimeSpan.Zero ||
                item.InvocationCount == 0 && (item.TotalDuration != TimeSpan.Zero || item.MaximumDuration != TimeSpan.Zero) ||
                item.InvocationCount > 0 && (item.TotalDuration == TimeSpan.Zero || item.MaximumDuration > item.TotalDuration)) ||
            snapshot.OperationalHistory is null || !IsValid(snapshot.OperationalHistory) ||
            snapshot.ProjectionRevision < 0 || snapshot.NodeCount < 0 ||
            snapshot.EdgeCount < 0 || snapshot.EvidenceCount < 0 || snapshot.DecisionCount < 0 ||
            snapshot.RepairRequiredCount < 0 || snapshot.RecoveredClaimCount < 0 || snapshot.QueueLength < 0 ||
            !GraphBoundaryValidator.IsValid(snapshot.Coverage) ||
            snapshot.RunId is { } runId && !IsSafeCategory(runId, GraphLimits.MaximumStableIdCharacters) ||
            snapshot.LastFailureCategory is { } category && !IsSafeDiagnosticCode(category, 128) ||
            !IsValid(snapshot.Maintenance))
        {
            throw new GraphAccessUnavailableException("graph-diagnostics-invalid");
        }

        try
        {
            GraphProjectionCoordinator.ValidateStorageBreakdown(snapshot.StorageBreakdown);
        }
        catch (GraphAccessUnavailableException)
        {
            throw new GraphAccessUnavailableException("graph-diagnostics-invalid");
        }
        if (_storageLifecycle is not null)
        {
            var storage = await _storageLifecycle.GetStorageBreakdownAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                GraphProjectionCoordinator.ValidateStorageBreakdown(storage);
            }
            catch (GraphAccessUnavailableException)
            {
                throw new GraphAccessUnavailableException("graph-diagnostics-invalid");
            }
            snapshot = snapshot with { StorageBreakdown = storage };
        }

        return snapshot;
    }

    private static bool IsValid(GraphOperationalHistorySummary history)
    {
        if (history.BoundEventCount < 0 || history.QuotaEventCount < 0 || history.CancellationCount < 0 ||
            history.RecoveryCount < 0 || history.RepairCount < 0)
        {
            return false;
        }

        var hasHistory = history.BoundEventCount > 0 || history.QuotaEventCount > 0 || history.CancellationCount > 0 ||
            history.RecoveryCount > 0 || history.RepairCount > 0;
        if (history.LastEventAtUtc == default(DateTimeOffset))
        {
            return false;
        }

        return !hasHistory || history.LastEventAtUtc.HasValue;
    }

    private static bool IsValid(GraphMaintenanceStatus status)
    {
        if (status is null || status.LastRecordsRemoved < 0 ||
            status.LastCompletedAtUtc == default(DateTimeOffset) ||
            !string.IsNullOrEmpty(status.Message) && !IsSafeCategory(status.Message, GraphLimits.MaximumDecisionReasonCharacters))
        {
            return false;
        }

        return status.LastCompletedAtUtc is { } completed && completed != default ||
            (!status.QuotaBlocked && status.LastRecordsRemoved == 0);
    }

    private static bool IsSafeCategory(string value, int maximumLength) =>
        value.Length <= maximumLength && !string.IsNullOrWhiteSpace(value) &&
        !ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(value);

    private static bool IsSafeDiagnosticCode(string value, int maximumLength) =>
        IsSafeCategory(value, maximumLength) && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
}
