using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Projects stable mechanical graph components from bounded retained observations.</summary>
public sealed class DeterministicGraphProjectionBuilder : IGraphProjectionBuilder
{
    private const string Algorithm = "stable-mechanical-projection";
    private const string AlgorithmVersion = "1.0.0";
    private readonly IGraphIdentityResolver _identityResolver;

    /// <summary>Initializes the deterministic projection builder.</summary>
    /// <param name="identityResolver">Conservative stable identity resolver.</param>
    public DeterministicGraphProjectionBuilder(IGraphIdentityResolver identityResolver)
    {
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    }

    /// <inheritdoc />
    public GraphComponentProjection Build(
        GraphProjectionObservation observation,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSnapshot(snapshot);
        ValidateObservation(observation);
        if (validatedAtUtc == default)
        {
            throw new InvalidDataException("A graph projection validation timestamp is required.");
        }

        if (observation.IsExcluded)
        {
            return Empty(observation, snapshot, isDeletion: true);
        }

        return observation switch
        {
            GraphSourceObservation source => BuildSource(source, snapshot, validatedAtUtc),
            GraphFileObservation file => BuildFile(file, snapshot, validatedAtUtc),
            GraphRelationshipObservation relationship => BuildRelationship(relationship, snapshot, validatedAtUtc),
            GraphCollectionObservation collection => BuildCollection(collection, snapshot, validatedAtUtc),
            GraphCollectionMembershipObservation membership => BuildMembership(membership, snapshot, validatedAtUtc),
            GraphLegacyDecisionObservation decision => BuildLegacyDecision(decision, snapshot),
            GraphDeletionObservation deletion => Empty(deletion, snapshot, isDeletion: true),
            _ => throw new InvalidDataException("Unknown graph projection observation type."),
        };
    }

    private GraphComponentProjection BuildSource(
        GraphSourceObservation source,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc)
    {
        ValidateLabel(source.DisplayName, nameof(source.DisplayName));
        var identity = ResolveIdentity(new GraphIdentityInput
        {
            Kind = GraphNodeKind.Source,
            Scope = "source",
            CanonicalKey = source.SourceId,
            ExistingStableId = source.SourceId,
            NormalizationVersion = "existing-id-v1",
        });
        return Component(source, snapshot, nodes:
        [
            Node(
                identity,
                source.SourceId,
                source.DisplayName,
                GraphOrigin.Mechanical,
                source,
                snapshot,
                validatedAtUtc,
                source.SourceId),
        ]);
    }

    private GraphComponentProjection BuildFile(
        GraphFileObservation file,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc)
    {
        ValidateLabel(file.FileName, nameof(file.FileName));
        if (file.Length < 0)
        {
            throw new InvalidDataException("A graph file observation has a negative length.");
        }

        var fileIdentity = ResolveIdentity(new GraphIdentityInput
        {
            Kind = GraphNodeKind.File,
            Scope = "file",
            CanonicalKey = file.FileId,
            ExistingStableId = file.FileId,
            NormalizationVersion = "existing-id-v1",
        });
        var sourceIdentity = ResolveIdentity(new GraphIdentityInput
        {
            Kind = GraphNodeKind.Source,
            Scope = "source",
            CanonicalKey = file.SourceId,
            ExistingStableId = file.SourceId,
            NormalizationVersion = "existing-id-v1",
        });
        var folderIdentity = ResolveIdentity(new GraphIdentityInput
        {
            Kind = GraphNodeKind.Folder,
            Scope = file.SourceId,
            CanonicalKey = file.FolderRelativePath,
            NormalizationVersion = file.PathSemanticsVersion,
            PathComparison = file.PathComparison,
        });

        var nodes = new List<GraphNode>(3)
        {
            Node(
                fileIdentity,
                file.FileId,
                file.FileName,
                GraphOrigin.Mechanical,
                file,
                snapshot,
                validatedAtUtc,
                file.SourceId),
            Node(
                folderIdentity,
                NormalizeRelativePath(file.FolderRelativePath, file.PathComparison),
                FolderLabel(file.FolderRelativePath),
                GraphOrigin.Mechanical,
                file,
                snapshot,
                validatedAtUtc,
                file.SourceId),
        };
        var evidence = new List<GraphEvidenceReference>(4);
        var edges = new List<GraphEdge>(3);
        var facts = new List<GraphFact>(3);

        var ownerEvidence = Evidence(file, snapshot, GraphEvidenceKind.SourceOwnership, "source-owner", "source-ownership", "Indexed file belongs to this source.");
        evidence.Add(ownerEvidence);
        edges.Add(Edge(
            GraphEdgeKind.OwnedBySource,
            fileIdentity.NodeId,
            sourceIdentity.NodeId,
            GraphConfidenceLevel.High,
            GraphOrigin.Mechanical,
            [ownerEvidence],
            file.CanonicalRowHash,
            file.ObservedAtUtc,
            validatedAtUtc));

        var folderEvidence = Evidence(file, snapshot, GraphEvidenceKind.RelativeFolder, "relative-folder", "relative-folder", "Indexed source-relative folder matched exactly.");
        evidence.Add(folderEvidence);
        edges.Add(Edge(
            GraphEdgeKind.LocatedInFolder,
            fileIdentity.NodeId,
            folderIdentity.NodeId,
            GraphConfidenceLevel.High,
            GraphOrigin.Mechanical,
            [folderEvidence],
            file.CanonicalRowHash,
            file.ObservedAtUtc,
            validatedAtUtc));

        var identityEvidence = Evidence(file, snapshot, GraphEvidenceKind.StableIdentity, "file-observation", "indexed-file-observation", "Retained indexed file observation.");
        evidence.Add(identityEvidence);
        facts.Add(Fact(fileIdentity.NodeId, GraphFactKind.FileSize, file.Length.ToString(CultureInfo.InvariantCulture), identityEvidence.Id));
        if (file.CreationTimeUtc is { } created)
        {
            facts.Add(Fact(fileIdentity.NodeId, GraphFactKind.CreatedTimestamp, created.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), identityEvidence.Id));
        }

        if (file.ModifiedTimeUtc is { } modified)
        {
            facts.Add(Fact(fileIdentity.NodeId, GraphFactKind.ModifiedTimestamp, modified.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), identityEvidence.Id));
        }

        if (!file.RelationshipAnalysisSuppressed && !string.IsNullOrWhiteSpace(file.ContentHash))
        {
            var documentSetIdentity = ResolveIdentity(new GraphIdentityInput
            {
                Kind = GraphNodeKind.DocumentSet,
                Scope = "content",
                CanonicalKey = file.ContentHash,
                HashAlgorithmVersion = file.ContentHashAlgorithmVersion,
                NormalizationVersion = "exact-content-v1",
            });
            var hash = file.ContentHash.ToLowerInvariant();
            nodes.Add(Node(
                documentSetIdentity,
                hash,
                string.Concat("Document set ", hash.AsSpan(0, Math.Min(12, hash.Length))),
                GraphOrigin.Mechanical,
                file,
                snapshot,
                validatedAtUtc));
            var hashEvidence = Evidence(file, snapshot, GraphEvidenceKind.ExactContentHash, "content-hash", "exact-content-hash", "Exact content fingerprint matched this document set.");
            evidence.Add(hashEvidence);
            edges.Add(Edge(
                GraphEdgeKind.SameDocumentSet,
                fileIdentity.NodeId,
                documentSetIdentity.NodeId,
                GraphConfidenceLevel.High,
                GraphOrigin.Mechanical,
                [hashEvidence],
                file.CanonicalRowHash,
                file.ObservedAtUtc,
                validatedAtUtc));
        }

        return Component(
            file,
            snapshot,
            nodes,
            edges,
            evidence,
            facts: facts,
            requiredNodeIds: [sourceIdentity.NodeId]);
    }

    private GraphComponentProjection BuildRelationship(
        GraphRelationshipObservation relationship,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc)
    {
        if (relationship.IsRejected)
        {
            return Empty(relationship, snapshot, isDeletion: true);
        }

        if (!relationship.IsManual && relationship.Evidence.Count == 0)
        {
            throw new InvalidDataException("An automatic graph edge requires retained evidence.");
        }

        if (relationship.Evidence.Count > GraphLimits.MaximumEvidencePerEdge)
        {
            throw new InvalidDataException("Relationship evidence exceeds the graph safety ceiling.");
        }

        var first = ResolveFile(relationship.FirstFileId);
        var second = ResolveFile(relationship.SecondFileId);
        if (first.NodeId == second.NodeId)
        {
            throw new InvalidDataException("A related-file self-loop is not supported.");
        }

        var source = string.CompareOrdinal(first.NodeId, second.NodeId) <= 0 ? first.NodeId : second.NodeId;
        var target = source == first.NodeId ? second.NodeId : first.NodeId;
        var retainedEvidence = relationship.Evidence
            .OrderBy(item => item.StableKey, StringComparer.Ordinal)
            .Select(item => Evidence(relationship, snapshot, item))
            .ToArray();
        var edge = Edge(
            GraphEdgeKind.RelatedFile,
            source,
            target,
            relationship.IsManual ? GraphConfidenceLevel.Confirmed : relationship.Confidence,
            GraphOrigin.LegacyRelationship,
            retainedEvidence,
            relationship.CanonicalRowHash,
            relationship.ObservedAtUtc,
            validatedAtUtc,
            relationship.Algorithm,
            relationship.AlgorithmVersion,
            relationship.IsManual);
        return Component(
            relationship,
            snapshot,
            edges: [edge],
            evidence: retainedEvidence,
            requiredNodeIds: [source, target]);
    }

    private GraphComponentProjection BuildCollection(
        GraphCollectionObservation collection,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc)
    {
        if (collection.IsForgotten)
        {
            return Empty(collection, snapshot, isDeletion: true);
        }

        ValidateLabel(collection.Title, nameof(collection.Title));
        var identity = ResolveIdentity(new GraphIdentityInput
        {
            Kind = GraphNodeKind.Collection,
            Scope = "collection",
            CanonicalKey = collection.CollectionId,
            ExistingStableId = collection.CollectionId,
            NormalizationVersion = "existing-id-v1",
        });
        return Component(collection, snapshot, nodes:
        [
            Node(identity, collection.CollectionId, collection.Title, GraphOrigin.LegacyCollection, collection, snapshot, validatedAtUtc),
        ]);
    }

    private GraphComponentProjection BuildMembership(
        GraphCollectionMembershipObservation membership,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc)
    {
        var file = ResolveFile(membership.FileId);
        var collection = ResolveIdentity(new GraphIdentityInput
        {
            Kind = GraphNodeKind.Collection,
            Scope = "collection",
            CanonicalKey = membership.CollectionId,
            ExistingStableId = membership.CollectionId,
            NormalizationVersion = "existing-id-v1",
        });
        var retained = Evidence(membership, snapshot, GraphEvidenceKind.CollectionMembership, "collection-member", "collection-membership", "Authoritative Smart Collection membership.");
        var edge = Edge(
            GraphEdgeKind.MemberOf,
            file.NodeId,
            collection.NodeId,
            membership.IsManual ? GraphConfidenceLevel.Confirmed : GraphConfidenceLevel.High,
            GraphOrigin.LegacyCollection,
            [retained],
            membership.CanonicalRowHash,
            membership.ObservedAtUtc,
            validatedAtUtc,
            isManual: membership.IsManual);
        return Component(
            membership,
            snapshot,
            edges: [edge],
            evidence: [retained],
            requiredNodeIds: [file.NodeId, collection.NodeId]);
    }

    private static GraphComponentProjection BuildLegacyDecision(
        GraphLegacyDecisionObservation decision,
        GraphProjectionSnapshot snapshot)
    {
        ValidateLabel(decision.DecisionNamespace, nameof(decision.DecisionNamespace));
        ValidateLabel(decision.ActionCode, nameof(decision.ActionCode));
        return Component(
            decision,
            snapshot,
            legacyDecisions:
            [
                new GraphLegacyDecisionMirror(
                    decision.DecisionNamespace,
                    decision.LegacyDecisionKey,
                    decision.ActionCode,
                    snapshot.LegacyDecisionManifestId,
                    decision.CanonicalRowHash,
                    decision.IsRetired),
            ]);
    }

    private GraphIdentity ResolveFile(string fileId) => ResolveIdentity(new GraphIdentityInput
    {
        Kind = GraphNodeKind.File,
        Scope = "file",
        CanonicalKey = fileId,
        ExistingStableId = fileId,
        NormalizationVersion = "existing-id-v1",
    });

    private GraphIdentity ResolveIdentity(GraphIdentityInput input)
    {
        var resolution = _identityResolver.Resolve(input);
        if (resolution.Status != GraphIdentityResolutionStatus.Resolved || resolution.NodeId is null)
        {
            throw new InvalidDataException(string.Concat("Graph identity was not safely resolved: ", resolution.ReasonCode));
        }

        return new GraphIdentity(
            resolution.NodeId,
            input.Kind,
            input.Scope,
            input.ExistingStableId ?? input.CanonicalKey,
            input.NormalizationVersion,
            resolution.CanonicalInputs);
    }

    private static GraphNode Node(
        GraphIdentity identity,
        string stableKey,
        string label,
        GraphOrigin origin,
        GraphProjectionObservation observation,
        GraphProjectionSnapshot snapshot,
        DateTimeOffset validatedAtUtc,
        string? owningSourceId = null) => new()
        {
            Identity = identity with { CanonicalKey = stableKey },
            DisplayLabel = label,
            OwningSourceId = owningSourceId,
            Origin = origin,
            SourceManifestId = snapshot.ManifestId,
            ObservationHash = observation.CanonicalRowHash,
            Algorithm = Algorithm,
            AlgorithmVersion = AlgorithmVersion,
            CreatedAtUtc = observation.ObservedAtUtc,
            LastValidatedAtUtc = validatedAtUtc,
        };

    private static GraphEvidenceReference Evidence(
        GraphProjectionObservation observation,
        GraphProjectionSnapshot snapshot,
        GraphProjectionEvidence evidence)
    {
        ValidateEvidence(evidence.EvidenceKey, evidence.ExplanationTemplateCode, evidence.Explanation, evidence.CanonicalObservationHash);
        return new GraphEvidenceReference
        {
            Id = StableId("evidence", evidence.StableKey, evidence.Kind.Value, evidence.EvidenceKey),
            Kind = evidence.Kind,
            SourceEvidenceKey = evidence.StableKey,
            ExplanationTemplateCode = evidence.ExplanationTemplateCode,
            Explanation = evidence.Explanation,
            SourceManifestId = snapshot.ManifestId,
            ObservationHash = evidence.CanonicalObservationHash,
        };
    }

    private static GraphEvidenceReference Evidence(
        GraphProjectionObservation observation,
        GraphProjectionSnapshot snapshot,
        GraphEvidenceKind kind,
        string keySuffix,
        string template,
        string explanation) => Evidence(
            observation,
            snapshot,
            new GraphProjectionEvidence(
                string.Concat(observation.StableKey, ":", keySuffix),
                kind,
                keySuffix,
                template,
                explanation,
                observation.CanonicalRowHash));

    private static GraphEdge Edge(
        GraphEdgeKind kind,
        string source,
        string target,
        GraphConfidenceLevel confidence,
        GraphOrigin origin,
        IReadOnlyList<GraphEvidenceReference> evidence,
        string inputFingerprint,
        DateTimeOffset createdAtUtc,
        DateTimeOffset validatedAtUtc,
        string algorithm = Algorithm,
        string algorithmVersion = AlgorithmVersion,
        bool isManual = false)
    {
        if (!isManual && evidence.Count == 0)
        {
            throw new InvalidDataException("An automatic graph edge must retain evidence.");
        }

        return new GraphEdge
        {
            Id = StableId("edge", kind.Value, source, target),
            Kind = kind,
            SourceNodeId = source,
            TargetNodeId = target,
            Confidence = confidence,
            Origin = origin,
            EvidenceIds = evidence.Select(item => item.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Algorithm = algorithm,
            AlgorithmVersion = algorithmVersion,
            InputFingerprint = inputFingerprint,
            CreatedAtUtc = createdAtUtc,
            LastValidatedAtUtc = validatedAtUtc,
            IsManual = isManual,
        };
    }

    private static GraphFact Fact(string nodeId, GraphFactKind kind, string value, string evidenceId) => new(
        StableId("fact", nodeId, kind.Value, value),
        nodeId,
        kind,
        value,
        [evidenceId],
        AlgorithmVersion);

    private static GraphComponentProjection Component(
        GraphProjectionObservation observation,
        GraphProjectionSnapshot snapshot,
        IReadOnlyList<GraphNode>? nodes = null,
        IReadOnlyList<GraphEdge>? edges = null,
        IReadOnlyList<GraphEvidenceReference>? evidence = null,
        IReadOnlyList<GraphAlias>? aliases = null,
        IReadOnlyList<GraphMention>? mentions = null,
        IReadOnlyList<GraphFact>? facts = null,
        IReadOnlyList<GraphLegacyDecisionMirror>? legacyDecisions = null,
        IReadOnlyList<string>? requiredNodeIds = null,
        bool isDeletion = false)
    {
        nodes ??= [];
        edges ??= [];
        evidence ??= [];
        aliases ??= [];
        mentions ??= [];
        facts ??= [];
        legacyDecisions ??= [];
        requiredNodeIds ??= [];
        if (nodes.Count > GraphLimits.MaximumComponentNodes || edges.Count > GraphLimits.MaximumComponentEdges ||
            aliases.Count > GraphLimits.MaximumAliasesPerNode || evidence.Any(item => item.Explanation.Length > GraphLimits.MaximumEvidenceTextCharacters))
        {
            throw new InvalidDataException("A graph component exceeds a hard safety ceiling.");
        }

        return new GraphComponentProjection
        {
            ComponentKey = string.Concat(observation.Kind.ToString(), ":", observation.StableKey),
            ObservationRevision = observation.Revision,
            InputFingerprint = StableHash(observation.StableKey, observation.CanonicalRowHash, snapshot.ManifestId, AlgorithmVersion),
            SourceManifestId = snapshot.ManifestId,
            Nodes = nodes.OrderBy(item => item.Identity.NodeId, StringComparer.Ordinal).ToArray(),
            Edges = edges.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Evidence = evidence.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Aliases = aliases.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Mentions = mentions.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Facts = facts.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            LegacyDecisions = legacyDecisions.OrderBy(item => item.LegacyDecisionKey, StringComparer.Ordinal).ToArray(),
            RequiredNodeIds = requiredNodeIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            IsDeletion = isDeletion,
        };
    }

    private static GraphComponentProjection Empty(
        GraphProjectionObservation observation,
        GraphProjectionSnapshot snapshot,
        bool isDeletion) => Component(observation, snapshot, isDeletion: isDeletion);

    private static string FolderLabel(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            return "Source root";
        }

        var separator = normalized.LastIndexOf('/');
        return normalized[(separator + 1)..];
    }

    private static string NormalizeRelativePath(string relativePath, GraphPathComparison comparison)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/').Normalize(NormalizationForm.FormC);
        return comparison == GraphPathComparison.CaseInsensitive ? normalized.ToUpperInvariant() : normalized;
    }

    private static void ValidateSnapshot(GraphProjectionSnapshot snapshot)
    {
        ValidateStable(snapshot.ManifestId, nameof(snapshot.ManifestId));
        ValidateStable(snapshot.LegacyDecisionManifestId, nameof(snapshot.LegacyDecisionManifestId));
        ValidateStable(snapshot.CanonicalManifestHash, nameof(snapshot.CanonicalManifestHash));
        if (snapshot.Revision < 0 || snapshot.PrivacySequence < 0 || snapshot.TotalObservationCount < 0 ||
            snapshot.CompletedAtUtc == default ||
            snapshot.ObservationCounts.Any(item => item.Count < 0) ||
            snapshot.ObservationCounts.Sum(item => item.Count) != snapshot.TotalObservationCount)
        {
            throw new InvalidDataException("The completed graph source manifest has invalid counts or revisions.");
        }
    }

    private static void ValidateObservation(GraphProjectionObservation observation)
    {
        ValidateStable(observation.StableKey, nameof(observation.StableKey));
        ValidateStable(observation.CanonicalRowHash, nameof(observation.CanonicalRowHash));
        if (observation.Revision < 0 || observation.ObservedAtUtc == default)
        {
            throw new InvalidDataException("A graph observation has a negative revision.");
        }
    }

    private static void ValidateStable(string value, string parameterName)
    {
        if (!ConservativeGraphIdentityResolver.TryNormalizeBounded(value, allowEmpty: false, out _))
        {
            throw new InvalidDataException(string.Concat("Invalid bounded graph value: ", parameterName));
        }
    }

    private static void ValidateLabel(string value, string parameterName)
    {
        if (value is null || value.Length > GraphLimits.MaximumLabelCharacters ||
            !ConservativeGraphIdentityResolver.TryNormalizeBounded(value, allowEmpty: false, out _))
        {
            throw new InvalidDataException(string.Concat("Invalid bounded graph label: ", parameterName));
        }
    }

    private static void ValidateEvidence(string key, string template, string explanation, string observationHash)
    {
        ValidateStable(key, nameof(key));
        ValidateStable(template, nameof(template));
        ValidateStable(observationHash, nameof(observationHash));
        if (explanation.Length > GraphLimits.MaximumEvidenceTextCharacters ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(explanation))
        {
            throw new InvalidDataException("Graph evidence contains invalid or oversized explanation text.");
        }
    }

    private static string StableId(string prefix, params string[] inputs) =>
        string.Concat("kg:", prefix, ":", StableHash(inputs));

    private static string StableHash(params string[] inputs)
    {
        var builder = new StringBuilder();
        foreach (var input in inputs)
        {
            builder.Append(input.Length).Append(':').Append(input).Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
