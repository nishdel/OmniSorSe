namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>
/// Validates graph-native manual control and delegates legacy-owned corrections without creating dual authority.
/// </summary>
public sealed class GraphDecisionService : IGraphDecisionService
{
    private readonly IGraphDecisionStore _decisionStore;
    private readonly IGraphStore _graphStore;
    private readonly IGraphIdentityResolver _identityResolver;
    private readonly IGraphReconciliationSignal _reconciliationSignal;
    private readonly IGraphProjectionSource? _projectionSource;
    private readonly TimeProvider _timeProvider;
    private readonly IGraphStorageLifecycle _storageLifecycle;
    private readonly IGraphLegacyAuthorityBridge? _legacyAuthorityBridge;

    /// <summary>Initializes the graph decision service and its optional legacy-authority bridge.</summary>
    public GraphDecisionService(
        IGraphDecisionStore decisionStore,
        IGraphStore graphStore,
        IGraphIdentityResolver identityResolver,
        IGraphReconciliationSignal reconciliationSignal,
        TimeProvider? timeProvider = null,
        IGraphProjectionSource? projectionSource = null,
        IGraphStorageLifecycle? storageLifecycle = null,
        IGraphLegacyAuthorityBridge? legacyAuthorityBridge = null)
    {
        _decisionStore = decisionStore ?? throw new ArgumentNullException(nameof(decisionStore));
        _graphStore = graphStore ?? throw new ArgumentNullException(nameof(graphStore));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _reconciliationSignal = reconciliationSignal ?? throw new ArgumentNullException(nameof(reconciliationSignal));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _projectionSource = projectionSource;
        _storageLifecycle = storageLifecycle ?? new AlwaysProvisionedGraphStorageLifecycle();
        _legacyAuthorityBridge = legacyAuthorityBridge;
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> ApplyAsync(
        GraphDecisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateSyntax(command);
        var keys = DecisionAuthorityKeys(command);
        var fence = await EnsureMutationAuthorityAsync(keys, null, cancellationToken).ConfigureAwait(false);
        if (fence.DecisionSequence != command.ExpectedSequence)
        {
            throw new GraphAccessUnavailableException("decision-checkpoint-stale");
        }

        await ValidateSemanticAsync(command, cancellationToken).ConfigureAwait(false);
        await EnsureMutationAuthorityAsync(keys, fence, cancellationToken).ConfigureAwait(false);

        var entry = await _decisionStore
            .AppendAsync(
                command with { ExpectedControlSettingsRevision = fence.ControlSettings.Revision },
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);
        var checkpoint = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(checkpoint) || checkpoint.Sequence < entry.Sequence)
        {
            throw new GraphAccessUnavailableException("decision-checkpoint-not-recoverable");
        }
        try
        {
            await _graphStore
                .InvalidateDecisionAsync(entry, checkpoint, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            await _reconciliationSignal.SignalAsync(cancellationToken).ConfigureAwait(false);
            return new GraphOperationResult(true, "The graph decision was saved and affected derived data was marked stale.", 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException or
                                          InvalidOperationException or GraphPersistenceException)
        {
            await _reconciliationSignal.SignalAsync(cancellationToken).ConfigureAwait(false);
            return new GraphOperationResult(
                true,
                "The graph decision is authoritative; derived reconciliation is pending selective repair.",
                1);
        }
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> CreateManualEntityAsync(
        string entityId,
        string label,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.CreateManualEntity,
                SubjectId = entityId,
                Label = label,
                NodeKind = GraphNodeKind.ManualEntity,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphOperationResult> RenameManualEntityAsync(
        string entityId,
        string label,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.RenameManualEntity,
                SubjectId = entityId,
                Label = label,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphOperationResult> AddAliasAsync(
        string entityId,
        string alias,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.AddAlias,
                SubjectId = entityId,
                Label = alias,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphOperationResult> RemoveAliasAsync(
        string entityId,
        string alias,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.RemoveAlias,
                SubjectId = entityId,
                Label = alias,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphOperationResult> LinkAsync(
        string sourceNodeId,
        string targetNodeId,
        string reason,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.LinkNodes,
                SubjectId = sourceNodeId,
                TargetId = targetNodeId,
                Reason = reason,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<GraphOperationResult> UnlinkAsync(
        string edgeId,
        bool preventRegeneration,
        CancellationToken cancellationToken = default)
    {
        GraphQueryService.ValidateId(edgeId);
        var initialFence = await EnsureMutationAuthorityAsync([edgeId], null, cancellationToken).ConfigureAwait(false);
        var edge = await _graphStore.GetEdgeAsync(edgeId, cancellationToken).ConfigureAwait(false);
        if (edge is null || !GraphBoundaryValidator.IsValid(edge))
        {
            throw new InvalidOperationException("The graph edge is missing, stale, or invalid.");
        }

        var source = await _graphStore.GetNodeAsync(edge.SourceNodeId, cancellationToken).ConfigureAwait(false);
        var target = await _graphStore.GetNodeAsync(edge.TargetNodeId, cancellationToken).ConfigureAwait(false);
        RequireVisible(source);
        RequireVisible(target);
        await EnsureMutationAuthorityAsync(
            [edgeId, edge.SourceNodeId, edge.TargetNodeId],
            initialFence,
            cancellationToken).ConfigureAwait(false);
        var legacyResult = await TryApplyLegacyAuthorityAsync(
            edge,
            source!,
            target!,
            preventRegeneration,
            cancellationToken).ConfigureAwait(false);
        if (legacyResult is not null)
        {
            return legacyResult;
        }

        var relationshipScope = !string.IsNullOrWhiteSpace(source!.Node.OwningSourceId) &&
            string.Equals(source.Node.OwningSourceId, target!.Node.OwningSourceId, StringComparison.Ordinal)
                ? source.Node.OwningSourceId
                : "cross-source";
        return await WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = preventRegeneration ? GraphDecisionKind.NeverMerge : GraphDecisionKind.UnlinkNodes,
                SubjectId = edgeId,
                Reason = preventRegeneration ? "prevent-regeneration" : "unlink",
                RelationshipSourceNodeId = edge.SourceNodeId,
                RelationshipTargetNodeId = edge.TargetNodeId,
                RelationshipKind = edge.Kind,
                RelationshipScope = relationshipScope,
                ExpectedSequence = sequence,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<GraphOperationResult> MergeAsync(
        string targetEntityId,
        string sourceEntityId,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.MergeEntities,
                SubjectId = targetEntityId,
                TargetId = sourceEntityId,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphOperationResult> SplitAsync(
        string entityId,
        string memberId,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.SplitEntities,
                SubjectId = entityId,
                TargetId = memberId,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<GraphOperationResult> RejectSuggestionAsync(
        string suggestionId,
        CancellationToken cancellationToken = default) => WithCurrentSequenceAsync(
            sequence => new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.RejectSuggestion,
                SubjectId = suggestionId,
                ExpectedSequence = sequence,
            },
            cancellationToken);

    private async Task<GraphOperationResult> WithCurrentSequenceAsync(
        Func<long, GraphDecisionCommand> commandFactory,
        CancellationToken cancellationToken)
    {
        var snapshot = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(snapshot))
        {
            throw new GraphAccessUnavailableException("decision-store-invalid");
        }

        return await ApplyAsync(commandFactory(snapshot.Sequence), cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateSemanticAsync(GraphDecisionCommand command, CancellationToken cancellationToken)
    {
        switch (command.Kind)
        {
            case GraphDecisionKind.CreateManualEntity:
                {
                    var resolution = _identityResolver.Resolve(new GraphIdentityInput
                    {
                        Kind = GraphNodeKind.ManualEntity,
                        Scope = "manual",
                        CanonicalKey = command.SubjectId,
                        ExistingStableId = command.SubjectId,
                        NormalizationVersion = "manual-id-v1",
                    });
                    if (resolution.Status != GraphIdentityResolutionStatus.Resolved || resolution.NodeId is null)
                    {
                        throw new InvalidOperationException("The manual entity identity is not valid.");
                    }

                    if (await _graphStore.GetNodeAsync(resolution.NodeId, cancellationToken).ConfigureAwait(false) is not null)
                    {
                        throw new InvalidOperationException("The manual entity already exists.");
                    }

                    break;
                }
            case GraphDecisionKind.RenameManualEntity:
            case GraphDecisionKind.AddAlias:
            case GraphDecisionKind.RemoveAlias:
                RequireManual(await _graphStore.GetNodeAsync(command.SubjectId, cancellationToken).ConfigureAwait(false));
                break;
            case GraphDecisionKind.MergeEntities:
            case GraphDecisionKind.SplitEntities:
                RequireManual(await _graphStore.GetNodeAsync(command.SubjectId, cancellationToken).ConfigureAwait(false));
                RequireManual(await _graphStore.GetNodeAsync(command.TargetId!, cancellationToken).ConfigureAwait(false));
                break;
            case GraphDecisionKind.LinkNodes:
                RequireVisible(await _graphStore.GetNodeAsync(command.SubjectId, cancellationToken).ConfigureAwait(false));
                RequireVisible(await _graphStore.GetNodeAsync(command.TargetId!, cancellationToken).ConfigureAwait(false));
                break;
            case GraphDecisionKind.UnlinkNodes:
            case GraphDecisionKind.NeverMerge:
                if (await _graphStore.GetEdgeAsync(command.SubjectId, cancellationToken).ConfigureAwait(false) is not { } edge)
                {
                    throw new InvalidOperationException("The graph edge is missing or stale.");
                }

                if (command.RelationshipSourceNodeId is not null &&
                    (!string.Equals(command.RelationshipSourceNodeId, edge.SourceNodeId, StringComparison.Ordinal) ||
                     !string.Equals(command.RelationshipTargetNodeId, edge.TargetNodeId, StringComparison.Ordinal) ||
                     command.RelationshipKind != edge.Kind))
                {
                    throw new InvalidOperationException("The relationship endpoints or kind changed before the decision was persisted.");
                }

                if (edge.Origin is GraphOrigin.LegacyRelationship or GraphOrigin.LegacyCollection)
                {
                    throw new InvalidOperationException(
                        "Legacy-owned relationships must be changed through the existing relationship authority.");
                }

                break;
            case GraphDecisionKind.RejectSuggestion:
                if (await _graphStore.GetMentionAsync(command.SubjectId, cancellationToken).ConfigureAwait(false) is not { IsConfirmed: false })
                {
                    throw new InvalidOperationException("The graph suggestion is missing, stale, or already confirmed.");
                }

                break;
        }
    }

    private async Task<GraphOperationResult?> TryApplyLegacyAuthorityAsync(
        GraphEdge edge,
        GraphNodeDetails source,
        GraphNodeDetails target,
        bool preventRegeneration,
        CancellationToken cancellationToken)
    {
        if (edge.Origin is not (GraphOrigin.LegacyRelationship or GraphOrigin.LegacyCollection))
        {
            return null;
        }

        if (_legacyAuthorityBridge is null)
        {
            throw new GraphAccessUnavailableException("legacy-authority-bridge-unconfigured");
        }

        GraphOperationResult result;
        if (edge.Origin == GraphOrigin.LegacyRelationship && edge.Kind == GraphEdgeKind.RelatedFile &&
            source.Node.Identity.Kind == GraphNodeKind.File && target.Node.Identity.Kind == GraphNodeKind.File)
        {
            result = await _legacyAuthorityBridge
                .UnlinkRelationshipAsync(
                    source.Node.Identity.CanonicalKey,
                    target.Node.Identity.CanonicalKey,
                    preventRegeneration,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (edge.Origin == GraphOrigin.LegacyCollection && edge.Kind == GraphEdgeKind.MemberOf &&
                 TryGetCollectionMembership(source.Node, target.Node, out var collectionId, out var fileId))
        {
            result = await _legacyAuthorityBridge
                .SplitCollectionMemberAsync(collectionId, fileId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException(
                "This legacy-owned graph edge has no safe mutation in the existing relationship authority.");
        }

        if (!result.Succeeded)
        {
            return result;
        }

        try
        {
            await _reconciliationSignal.SignalAsync(CancellationToken.None).ConfigureAwait(false);
            return result with
            {
                Message = $"{result.Message} Knowledge Graph reconciliation was scheduled from the authoritative change.",
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException or
                                          InvalidOperationException or GraphPersistenceException)
        {
            return result with
            {
                Message = $"{result.Message} The authoritative change is saved; Knowledge Graph reconciliation remains pending.",
            };
        }
    }

    private static bool TryGetCollectionMembership(
        GraphNode first,
        GraphNode second,
        out string collectionId,
        out string fileId)
    {
        var collection = first.Identity.Kind == GraphNodeKind.Collection ? first : second;
        var file = first.Identity.Kind == GraphNodeKind.File ? first : second;
        if (collection.Identity.Kind != GraphNodeKind.Collection || file.Identity.Kind != GraphNodeKind.File)
        {
            collectionId = string.Empty;
            fileId = string.Empty;
            return false;
        }

        collectionId = collection.Identity.CanonicalKey;
        fileId = file.Identity.CanonicalKey;
        return true;
    }

    private async Task<GraphReadAuthorityFence> EnsureMutationAuthorityAsync(
        IReadOnlyList<string> stableKeys,
        GraphReadAuthorityFence? expected,
        CancellationToken cancellationToken)
    {
        await GraphStorageAccessGate.EnsureProvisionedAsync(_storageLifecycle, cancellationToken).ConfigureAwait(false);
        if (_projectionSource is null)
        {
            throw new GraphAccessUnavailableException("authority-gate-unconfigured");
        }

        GraphDecisionSnapshot decisions;
        GraphControlSettings settings;
        try
        {
            decisions = await _decisionStore.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            settings = await _decisionStore.GetControlSettingsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new GraphAccessUnavailableException("decision-store-unavailable");
        }

        if (!GraphBoundaryValidator.IsValid(decisions))
        {
            throw new GraphAccessUnavailableException("decision-store-invalid");
        }
        GraphResourceAdmissionPolicy.Validate(settings);
        if (!settings.IsEnabled)
        {
            throw new GraphAccessUnavailableException("graph-disabled");
        }

        var authority = await _projectionSource.ValidateAuthorityAsync(
            new GraphAuthorityRequest(stableKeys, "manual-decision"),
            cancellationToken).ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(authority) || !authority.IsAvailable || !authority.IsAllowed)
        {
            throw new GraphAccessUnavailableException(authority.ReasonCode);
        }

        if (expected is not null &&
            (decisions.Sequence != expected.DecisionSequence ||
             !string.Equals(decisions.CheckpointId, expected.DecisionCheckpointId, StringComparison.Ordinal) ||
             settings != expected.ControlSettings ||
             authority.PrivacySequence != expected.PrivacySequence ||
             !string.Equals(authority.LegacyDecisionManifestId, expected.LegacyDecisionManifestId, StringComparison.Ordinal) ||
             !string.Equals(authority.CurrentSourceManifestId, expected.SourceManifestId, StringComparison.Ordinal) ||
             authority.CurrentSourceRevision != expected.SourceRevision))
        {
            throw new GraphAccessUnavailableException("decision-checkpoint-changed");
        }

        var coverage = await _graphStore.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        if (!GraphBoundaryValidator.IsValid(coverage) || !coverage.IsEnabled || !coverage.IsAvailable || coverage.IsStale ||
            !string.Equals(coverage.AppliedManifestId, authority.CurrentSourceManifestId, StringComparison.Ordinal) ||
            coverage.AppliedRevision != authority.CurrentSourceRevision ||
            coverage.AppliedDecisionSequence != decisions.Sequence ||
            !string.Equals(coverage.AppliedDecisionCheckpointId, decisions.CheckpointId, StringComparison.Ordinal) ||
            coverage.AppliedPrivacySequence < authority.PrivacySequence)
        {
            throw new GraphAccessUnavailableException("manual-decision-authority-pending");
        }

        var current = new GraphReadAuthorityFence(
            decisions.Sequence,
            decisions.CheckpointId,
            authority.PrivacySequence,
            authority.LegacyDecisionManifestId,
            authority.CurrentSourceManifestId,
            authority.CurrentSourceRevision,
            coverage.AppliedRevision,
            settings);
        if (expected is not null && current != expected)
        {
            throw new GraphAccessUnavailableException("decision-checkpoint-changed");
        }

        return current;
    }

    private static IReadOnlyList<string> DecisionAuthorityKeys(GraphDecisionCommand command) =>
        new[]
        {
            command.SubjectId,
            command.TargetId,
            command.RelationshipSourceNodeId,
            command.RelationshipTargetNodeId,
        }
        .Where(item => item is not null)
        .Cast<string>()
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static void RequireManual(GraphNodeDetails? details)
    {
        RequireVisible(details);
        if (details!.Node.Identity.Kind != GraphNodeKind.ManualEntity || details.Node.Origin != GraphOrigin.Manual)
        {
            throw new InvalidOperationException("Only compatible manual entities may be renamed, aliased, merged, or split.");
        }
    }

    private static void RequireVisible(GraphNodeDetails? details)
    {
        if (details is null || !GraphBoundaryValidator.IsValid(details.Node) ||
            !details.Node.IsVisible || details.Node.Integrity != GraphIntegrityState.Valid)
        {
            throw new InvalidOperationException("The graph identity is missing, excluded, or requires repair.");
        }
    }

    private static void ValidateSyntax(GraphDecisionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Kind) || command.ExpectedSequence < 0 || command.ExpectedControlSettingsRevision < 0)
        {
            throw new ArgumentException("The graph decision kind or expected sequence is invalid.", nameof(command));
        }

        GraphQueryService.ValidateId(command.SubjectId);
        if (command.TargetId is { } target)
        {
            GraphQueryService.ValidateId(target);
            if (string.Equals(command.SubjectId, target, StringComparison.Ordinal))
            {
                throw new ArgumentException("A graph decision cannot target the same identity twice.", nameof(command));
            }
        }

        if (command.Label is { } label)
        {
            GraphQueryService.ValidateBounded(label, GraphLimits.MaximumLabelCharacters, allowEmpty: false);
        }

        if (command.Reason is { } reason)
        {
            GraphQueryService.ValidateBounded(reason, GraphLimits.MaximumDecisionReasonCharacters, allowEmpty: false);
        }
        if (command.RelationshipSourceNodeId is { } relationshipSource)
        {
            GraphQueryService.ValidateId(relationshipSource);
        }
        if (command.RelationshipTargetNodeId is { } relationshipTarget)
        {
            GraphQueryService.ValidateId(relationshipTarget);
        }
        if (command.RelationshipKind is { } relationshipKind && !relationshipKind.IsStable)
        {
            throw new ArgumentException("The relationship decision kind is not stable.", nameof(command));
        }
        if (command.RelationshipScope is { } relationshipScope)
        {
            GraphQueryService.ValidateBounded(relationshipScope, GraphLimits.MaximumStableIdCharacters, allowEmpty: false);
        }

        switch (command.Kind)
        {
            case GraphDecisionKind.CreateManualEntity when command.NodeKind != GraphNodeKind.ManualEntity || command.Label is null:
                throw new ArgumentException("Manual entity creation requires the Manual Entity kind and a label.", nameof(command));
            case GraphDecisionKind.RenameManualEntity or GraphDecisionKind.AddAlias or GraphDecisionKind.RemoveAlias when command.Label is null:
                throw new ArgumentException("This graph decision requires a bounded label or alias.", nameof(command));
            case GraphDecisionKind.MergeEntities or GraphDecisionKind.SplitEntities or GraphDecisionKind.LinkNodes when command.TargetId is null:
                throw new ArgumentException("This graph decision requires a distinct target identity.", nameof(command));
            case GraphDecisionKind.UnlinkNodes or GraphDecisionKind.NeverMerge
                when new object?[]
                {
                    command.RelationshipSourceNodeId,
                    command.RelationshipTargetNodeId,
                    command.RelationshipKind,
                    command.RelationshipScope,
                }.Count(item => item is not null) is > 0 and < 4:
                throw new ArgumentException("A relationship-removal identity must be either legacy edge-only or a complete endpoint, kind, and scope tuple.", nameof(command));
        }
    }
}
